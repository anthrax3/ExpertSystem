﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Logikek.Language;
using Sprache;

namespace Logikek
{
    public static class Processor
    {
        private static List<Fact> _facts;
        private static List<Query> _queries;
        private static List<Rule> _rules;

        public static ProcessResult Run(string code)
        {
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
                errors.Add(new ParseError(ruleResult.Message + $", ожидалось {expected}", counter, column));
            }

            var queryResults = _queries.Select(ResolveQuery).ToList();

            return errors.Any() ? new ProcessResult(errors) : new ProcessResult(queryResults);
        }

        public static ProcessResult EvaluateQuery(string code)
        {
            var query = Logikek.Grammar.Query.TryParse(code.Trim());

            if (query.WasSuccessful)
            {
                var queriesList = new List<QueryResult> {ResolveQuery(query.Value)};
                return new ProcessResult(queriesList);
            }

            var errorList = new List<ParseError> {new ParseError(query.Message, 0, query.Remainder.Column)};
            return new ProcessResult(errorList);
        }

        private static QueryResult ResolveQuery(Query query)
        {
            if (query.IsSimple)
            {
                // Ищем факт с именем запроса 
                // И таким же набором аргументов (порядок важен)
                if (_facts.Any(fact => fact.Name == query.Name && fact.Arguments.SequenceEqual(query.Arguments)))
                {
                    return new QueryResult(true, query);
                }
            }

            return new QueryResult(false, query);
        }
    }
}
