using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class CliSearchTextTests
    {
        /// <summary>
        /// Simulates the text search matching logic used by CLI --search-text.
        /// Returns entries whose text contains the query (case-insensitive substring match).
        /// </summary>
        static List<(int index, string text)> SearchEntries(
            IReadOnlyList<string> entries, string query)
        {
            if (string.IsNullOrEmpty(query))
                return new List<(int, string)>();

            var results = new List<(int index, string text)>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i]) &&
                    entries[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add((i, entries[i]));
                }
            }
            return results;
        }

        private static readonly string[] SampleEntries = new[]
        {
            "Eirika",
            "Ephraim",
            "Seth",
            "Franz",
            "Gilliam",
            "Vanessa",
            "Moulder",
            "Ross",
            "Garcia",
            "Neimi",
        };

        [Fact]
        public void CaseInsensitiveMatch()
        {
            var results = SearchEntries(SampleEntries, "eirika");
            Assert.Single(results);
            Assert.Equal("Eirika", results[0].text);
            Assert.Equal(0, results[0].index);
        }

        [Fact]
        public void SubstringMatch()
        {
            // "rik" should match "Eirika"
            var results = SearchEntries(SampleEntries, "rik");
            Assert.Single(results);
            Assert.Equal("Eirika", results[0].text);
        }

        [Fact]
        public void NoMatch()
        {
            var results = SearchEntries(SampleEntries, "xyz");
            Assert.Empty(results);
        }

        [Fact]
        public void EmptyQuery_MatchesNothing()
        {
            var results = SearchEntries(SampleEntries, "");
            Assert.Empty(results);

            var resultsNull = SearchEntries(SampleEntries, null!);
            Assert.Empty(resultsNull);
        }

        [Fact]
        public void MultipleMatches()
        {
            // "a" appears in multiple entries
            var results = SearchEntries(SampleEntries, "a");
            var matchedNames = results.Select(r => r.text).ToList();
            Assert.Contains("Eirika", matchedNames);
            Assert.Contains("Ephraim", matchedNames);
            Assert.Contains("Franz", matchedNames);
            Assert.Contains("Gilliam", matchedNames);
            Assert.Contains("Vanessa", matchedNames);
            Assert.Contains("Garcia", matchedNames);
        }

        [Fact]
        public void MatchPreservesIndex()
        {
            var results = SearchEntries(SampleEntries, "Seth");
            Assert.Single(results);
            Assert.Equal(2, results[0].index); // Seth is at index 2
        }
    }
}
