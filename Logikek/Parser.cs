﻿using System.Collections.Generic;
using System.Linq;
using Logikek.Language;
using Sprache;

namespace Logikek
{
    public static class Parser
    {
        private static List<Fact> _facts;
        private static List<Query> _queries;
        private static List<Rule> _rules;

        private static Dictionary<Query, QueryResult> _cache;

        /// <summary>
        /// Анализирует строку исходного кода Logikek и возвращает объект с результатами
        /// </summary>
        /// <param name="code">Исходный код</param>
        /// <returns></returns>
        public static ProcessResult Run(string code)
        {
            _cache = new Dictionary<Query, QueryResult>();

            var errors = new List<ParseError>();

            _facts = new List<Fact>();
            _queries = new List<Query>();
            _rules = new List<Rule>();

            var lines = code.Split('\n').Select(line => line.Trim());

            var counter = 0;
            foreach (var line in lines)
            {
                counter++;
                if (string.IsNullOrEmpty(line)) continue;

                var factResult = Grammar.Fact.TryParse(line);
                if (factResult.WasSuccessful)
                {
                    if (factResult.Value.Arguments.Any(fact => fact.IsAtom))
                        errors.Add(new ParseError("факты не могут содержать атомы", counter, factResult.Remainder.Column));
                    else
                        _facts.Add(factResult.Value);

                    continue;
                }

                var queryResult = Grammar.Query.TryParse(line);
                if (queryResult.WasSuccessful)
                {
                    _queries.Add(queryResult.Value);
                    continue;
                }

                var ruleResult = Grammar.Rule.TryParse(line);
                if (ruleResult.WasSuccessful)
                {
                    _rules.Add(ruleResult.Value);
                    continue;
                }

                var commentResult = Grammar.Comment.TryParse(line);
                if (commentResult.WasSuccessful) continue;

                var expected = ruleResult.Expectations.FirstOrDefault();
                var column = ruleResult.Remainder.Column;
                errors.Add(new ParseError(string.Format("{0}{1}", ruleResult.Message,
                    string.Format(", ожидалось {0}", expected)), counter, column));
            }

            var queryResults = _queries.Select(ResolveQuery).ToList();

            return errors.Any() ? new ProcessResult(errors) : new ProcessResult(queryResults);
        }

        /// <summary>
        /// Вычисляет запрос и возвращает результат
        /// </summary>
        /// <param name="code">Строка с запросом</param>
        /// <returns></returns>
        public static ProcessResult EvaluateQuery(string code)
        {
            var query = Grammar.Query.TryParse(code.Trim());

            if (query.WasSuccessful)
            {
                var queriesList = new List<QueryResult> {ResolveQuery(query.Value)};
                return new ProcessResult(queriesList);
            }

            var errorList = new List<ParseError> {new ParseError(query.Message, 0, query.Remainder.Column)};
            return new ProcessResult(errorList);
        }

        private static QueryResult AddToCacheAndReturn(Query query, QueryResult result)
        {
            _cache[query] = result;
            return result;
        }

        private static QueryResult ResolveQuery(Query query)
        {
            if (_cache.ContainsKey(query))
            {
                if (_cache[query] == null)
                {
                    if (query.HasAtoms)
                    {
                        _cache[query] = new QueryResult(false, query, new List<Dictionary<string, string>>());
                    }
                    else
                    {
                        _cache[query] = new QueryResult(false, query);
                    }
                }
                return _cache[query];
            }

            _cache.Add(query, null);

            if (!query.HasAtoms) // Если нет атомов, то запрос простой (возвращает true или false)
            {
                // Попытка 1:
                // Ищем факт с именем запроса 
                // И таким же набором аргументов (порядок важен)
                if (_facts.Any(fact => fact.Name == query.Name && fact.Arguments.SequenceEqual(query.Arguments)))
                {
                    return AddToCacheAndReturn(query, new QueryResult(true, query));
                }

                // Попытка 2:
                // Найти все правила с именем запроса 
                // И аналогичным количеством аргументов
                var matchingRules = _rules.FindAll(rule => rule.Name == query.Name
                                                           &&
                                                           rule.Arguments.Count == query.Arguments.Count);

                // Если есть такие правила, то играем в дедукцию
                // Подставляем каждому правилу вместо атомов аргументы запроса 
                // И рекурсивно вычисляем каждое условие
                if (matchingRules.Any())
                {
                    foreach (var rule in matchingRules)
                    {
                        QueryResult finalResult = null;
                        foreach (var condition in rule.Conditions)
                        {
                            // Подставляем вместо атомов аргументы запроса
                            var conditionArgs = ReplaceAtomsWithNames(rule.Arguments, query.Arguments,
                                condition.Condition.Arguments);

                            // Вычисляем значение запроса
                            var conditionQuery = new Query(condition.Condition.Name, conditionArgs);

                            QueryResult queryResult;
                            if (_cache.ContainsKey(conditionQuery))
                            {
                                if (_cache[query] == null)
                                {
                                    return AddToCacheAndReturn(query, new QueryResult(false, query));
                                }
                                queryResult = _cache[query];
                            }
                            else
                            {
                                queryResult = ResolveQuery(conditionQuery);
                            }

                            if (condition.Condition.IsNegated)
                            {
                                if (queryResult.Solutions != null)
                                {
                                    queryResult = new QueryResult(!queryResult.Result, queryResult.TheQuery, queryResult.Solutions);
                                }
                                else
                                {
                                    queryResult = new QueryResult(!queryResult.Result, queryResult.TheQuery);
                                }
                            }

                            // Применяем логический оператор
                            finalResult = ApplyLogicalOperator(finalResult, condition.Operator, queryResult);
                        }
                        if (finalResult != null && finalResult.Result)
                        {
                            return AddToCacheAndReturn(query, new QueryResult(finalResult.Result, query));
                        }
                    }
                }

                // Попытка 3:
                // Не помогла дедукция -- не беда, пробуем индукцию
                // Ищем все правила, в которых наш запрос содержится в качестве условия
                var containingRules = _rules.Where(rule => rule.Conditions.Any(cnd => cnd.Condition.Name == query.Name
                                                                                      &&
                                                                                      cnd.Condition.Arguments.Count ==
                                                                                      query.Arguments.Count))
                    // Отсеиваем правила, которые содержат условия с оператором ИЛИ
                    .Where(rule => rule.Conditions.All(cnd => cnd.Operator != ConditionOperator.Or));

                foreach (var rule in containingRules)
                {
                    foreach (var condition in rule.Conditions)
                    {
                        if (condition.Condition.Name == query.Name && condition.Condition.Arguments.Count ==
                            query.Arguments.Count)
                        {
                            if (CompareArgumentsIgnoringAtoms(condition.Condition.Arguments, query.Arguments))
                            {
                                var nextQuery = new Query(rule.Name,
                                    ReplaceAtomsWithNames(condition.Condition.Arguments, query.Arguments, rule.Arguments));

                                QueryResult result;
                                if (_cache.ContainsKey(nextQuery))
                                {
                                    if (_cache[nextQuery] == null)
                                    {
                                        return AddToCacheAndReturn(query, new QueryResult(false, query));
                                    }
                                    result = _cache[nextQuery];
                                }
                                else
                                {
                                    result = ResolveQuery(nextQuery);
                                }

                                if (result.Result != condition.Condition.IsNegated)
                                {
                                    return AddToCacheAndReturn(query, new QueryResult(true, query));
                                }
                            }
                        }
                    }
                }
            }
            else // Атомы есть
            {
                var solutions = new List<Dictionary<string, string>>();

                // Шаг 1:
                // Найти все факты с именем запроса и нужным количеством аргументов
                var matchingFacts = _facts.FindAll(fact => fact.Name == query.Name
                                                           &&
                                                           fact.Arguments.Count == query.Arguments.Count)
                    // И взять только те, у которых идентичны аргументы, не являющиеся атомами
                    .Where(fact => CompareArgumentsIgnoringAtoms(query.Arguments, fact.Arguments));

                foreach (var fact in matchingFacts)
                {
                    solutions.Add(new Dictionary<string, string>());
                    for (var i = 0; i < query.Arguments.Count; i++)
                    {
                        var arg = query.Arguments.ElementAt(i);
                        if (arg.IsAtom)
                        {
                            var solution = fact.Arguments.ElementAt(i);
                            solutions.Last().Add(arg.Name, solution.Name);
                        }
                    }
                }

                /*
                Friends(X, Y) : Likes(X, Y) AND Knows(X, Y);

                Likes(Max, Jane);
                Knows(Max, Jane);

                Friends(X, Y)?
                */

                // Шаг 2:
                // Пытаемся вычислить все правила с именем запроса
                var matchingRules = _rules.Where(rule => rule.Name == query.Name
                                                         &&
                                                         rule.Arguments.Count == query.Arguments.Count);

                foreach (var rule in matchingRules)
                {
                    var ruleQuery = new Query(rule.Name, ReplaceAtomsWithNames(rule.Arguments, query.Arguments, rule.Arguments));

                    var queryResult = ResolveQuery(ruleQuery);
                    if (queryResult.Result)
                    {
                        solutions.Add(new Dictionary<string, string>());
                        for (var i = 0; i < query.Arguments.Count; i++)
                        {
                            var arg = query.Arguments.ElementAt(i);
                            if (arg.IsAtom)
                            {
                                var solution = ruleQuery.Arguments.ElementAt(i);
                                solutions.Last().Add(arg.Name, solution.Name);
                            }
                        }
                    }
                }

                return AddToCacheAndReturn(query, new QueryResult(solutions.Any(), query, solutions));
            }

            return AddToCacheAndReturn(query, new QueryResult(false, query));
        }

        private static bool CompareArgumentsIgnoringAtoms(List<ClauseArgument> original, List<ClauseArgument> another)
        {
            // Каждый элемент первого списка должен быть равен 
            // элементу второго списка (или быть атомом)
            return !original.Where((t, i) => !original.ElementAt(i).IsAtom
                                             &&
                                             original.ElementAt(i).Name != another.ElementAt(i).Name)
                .Any();
        }

        private static string Stringify(Dictionary<string, string> d)
        {
            return d.Keys.Aggregate("", (current, key) => current + (key + "=" + d[key] + ";"));
        }

        private static QueryResult ApplyLogicalOperator(QueryResult v1, ConditionOperator? @operator, QueryResult v2)
        {
            if (v1 == null)
            {
                return v2;
            }

            if (@operator == ConditionOperator.And)
            {
                IEnumerable<Dictionary<string, string>> solutions;
                bool includeSolutions = true;
                if (v1.Solutions != null)
                {
                    if (v2.Solutions != null)
                    {
                        var v2Solutions = new List<string>();
                        v2.Solutions.ForEach(solution => v2Solutions.Add(Stringify(solution)));

                        solutions = v1.Solutions.Where(s => v2Solutions.Contains(Stringify(s)));
                    }
                    else
                    {
                        solutions = v1.Solutions;
                    }
                }
                else
                {
                    if (v2.Solutions != null)
                    {
                        solutions = v2.Solutions;
                    }
                    else
                    {
                        includeSolutions = false;
                        solutions = new List<Dictionary<string, string>>();
                    }
                }

                if (includeSolutions)
                {
                    var solutionList = solutions.ToList();
                    return new QueryResult(solutionList.Any(), v2.TheQuery, solutionList);
                }
                return new QueryResult(v1.Result & v2.Result, v2.TheQuery);
            }
            if (v1.Result) return v1;
            if (v2.Result) return v2;
            return new QueryResult(false, v2.TheQuery);
        }

        private static List<ClauseArgument> ReplaceAtomsWithNames(List<ClauseArgument> atoms,
            List<ClauseArgument> names, List<ClauseArgument> @in)
        {
            var argumentMappings = new Dictionary<string, ClauseArgument>();
            var counter = 0;
            foreach (var ruleArg in atoms)
            {
                if (ruleArg.IsAtom)
                {
                    argumentMappings.Add(ruleArg.Name, names.ElementAt(counter));
                }
                counter++;
            }

            var result = new List<ClauseArgument>();
            @in.ForEach(result.Add);

            for (var i = 0; i < result.Count; i++)
            {
                if (result.ElementAt(i).IsAtom && argumentMappings.ContainsKey(result.ElementAt(i).Name))
                {
                    var arg = argumentMappings[result.ElementAt(i).Name];
                    result.RemoveAt(i);
                    result.Insert(i, arg);
                }
            }

            return result;
        }
    }
}