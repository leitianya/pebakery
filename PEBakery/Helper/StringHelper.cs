﻿/*
    Copyright (C) 2016-2018 Hajin Jang
    Licensed under MIT License.
 
    MIT License

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PEBakery.Helper
{
    #region StringHelper
    public static class StringHelper
    {
        /// <summary>
        /// Remove last newline in the string, removes whitespaces also.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string RemoveLastNewLine(string str)
        {
            return str.Trim().TrimEnd(Environment.NewLine.ToCharArray()).Trim();
        }

        public static bool IsHex(string str)
        {
            if (str.Length % 2 == 1)
                return false;

            return Regex.IsMatch(str, @"^[A-Fa-f0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public static bool IsUpperAlphabet(string str)
        {
            return Regex.IsMatch(str, @"^[A-Z]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public static bool IsUpperAlphabet(char ch)
        {
            return 'A' <= ch && ch <= 'Z';
        }

        public static bool IsLowerAlphabet(string str)
        {
            str = str.Trim();
            return Regex.IsMatch(str, @"^[a-z]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public static bool IsLowerAlphabet(char ch)
        {
            return 'a' <= ch && ch <= 'z';
        }

        public static bool IsAlphabet(string str)
        {
            return Regex.IsMatch(str, @"^[A-Za-z]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public static bool IsAlphabet(char ch)
        {
            return 'A' <= ch && ch <= 'Z' || 'a' <= ch && ch <= 'z';
        }

        public static bool IsWildcard(string str)
        {
            return (str.IndexOfAny(new[] { '*', '?' }) != -1);
        }

        /// <summary>
        /// Count occurrences of strings.
        /// http://www.dotnetperls.com/string-occurrence
        /// </summary>
        public static int CountOccurrences(string text, string pattern)
        {
            // Loop through all instances of the string 'text'.
            int count = 0;
            int i = 0;
            while ((i = text.IndexOf(pattern, i, StringComparison.Ordinal)) != -1)
            {
                i += pattern.Length;
                count++;
            }
            return count;
        }

        public static string ReplaceEx(string str, string oldValue, string newValue, StringComparison comp)
        {
            if (oldValue.Equals(string.Empty, comp))
                return str;

            if (str.IndexOf(oldValue, comp) == -1)
                return str;

            int idx = 0;
            StringBuilder b = new StringBuilder();
            while (idx < str.Length)
            {
                int vIdx = str.IndexOf(oldValue, idx, comp);
                if (vIdx == -1)
                {
                    b.Append(str.Substring(idx));
                    break;
                }

                b.Append(str.Substring(idx, vIdx - idx));
                b.Append(newValue);
                idx = vIdx + oldValue.Length;
            }
            return b.ToString();

        }

        public static string ReplaceRegex(string str, string regex, string newValue, StringComparison comp)
        {
            MatchCollection matches = Regex.Matches(str, regex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            if (matches.Count == 0)
                return str;

            StringBuilder b = new StringBuilder();
            for (int x = 0; x < matches.Count; x++)
            {
                if (x == 0)
                {
                    b.Append(str.Substring(0, matches[0].Index));
                }
                else
                {
                    int startOffset = matches[x - 1].Index + matches[x - 1].Value.Length;
                    int endOffset = matches[x].Index - startOffset;
                    b.Append(str.Substring(startOffset, endOffset));
                }

                b.Append(newValue);

                if (x + 1 == matches.Count)
                {
                    b.Append(str.Substring(matches[x].Index + matches[x].Value.Length));
                }
            }
            return b.ToString();
        }

        public static string ReplaceAt(string str, int index, int length, string newValue)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            return str.Substring(0, index) + newValue + str.Substring(index + length);
        }
    }
    #endregion
}