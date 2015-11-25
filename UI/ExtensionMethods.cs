﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace UI
{
    public static class ExtensionMethods
    {
        public static void AppendText(this RichTextBox box, string text, Color color)
        {
            var tr = new TextRange(box.Document.ContentEnd, box.Document.ContentEnd) {Text = text};
            tr.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
        }
    }
}
