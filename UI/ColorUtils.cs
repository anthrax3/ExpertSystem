using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UI
{
    public static class ColorUtils
    {
        /// <summary>
        /// �������� ���� �� ������������������ �������������
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static Color HexColor(string str)
        {
            return (Color) ColorConverter.ConvertFromString(str);
        }

        /// <summary>
        /// �������� �������� �����
        /// </summary>
        /// <param name="from">�������� ����</param>
        /// <param name="to">������� ����</param>
        /// <param name="targetName">��� Brush</param>
        /// <param name="reversed">��������</param>
        /// <param name="durationMs">�����������������</param>
        /// <param name="autoreverse">������������</param>
        /// <param name="repeat">������</param>
        /// <returns></returns>
        public static ColorAnimation CreateColorAnimation(Color from, Color to, string targetName, bool reversed,
            int durationMs = 175, bool autoreverse = false, bool repeat = false)
        {
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var animation = new ColorAnimation(reversed ? @from : to, reversed ? to : @from, duration)
            {
                AutoReverse = autoreverse
            };

            if (repeat) animation.RepeatBehavior = RepeatBehavior.Forever;

            Storyboard.SetTargetName(animation, targetName);
            Storyboard.SetTargetProperty(animation, new PropertyPath(SolidColorBrush.ColorProperty));

            return animation;
        }
    }
}