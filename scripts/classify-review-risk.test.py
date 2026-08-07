import unittest

from classify_review_risk import classify_paths


class ClassifyReviewRiskTests(unittest.TestCase):
    def test_empty_input_fails_closed(self):
        self.assertEqual("high", classify_paths([]))

    def test_docs_only_is_low(self):
        self.assertEqual(
            "low",
            classify_paths(["README.md", "docs/GUI-STRATEGY.md"]),
        )

    def test_normal_source_change_is_normal(self):
        self.assertEqual(
            "normal",
            classify_paths(["FEBuilderGBA.Core/TextEscape.cs"]),
        )

    def test_workflow_change_is_high(self):
        self.assertEqual(
            "high",
            classify_paths([".github/workflows/check.yml"]),
        )

    def test_rom_mutation_primitive_is_high(self):
        self.assertEqual(
            "high",
            classify_paths(["FEBuilderGBA.Core/Undo.cs"]),
        )

    def test_build_hook_is_high(self):
        self.assertEqual(
            "high",
            classify_paths(["FEBuilderGBA.Core/FEBuilderGBA.Core.csproj"]),
        )

    def test_repository_script_is_high(self):
        self.assertEqual(
            "high",
            classify_paths(["scripts/validate-something.sh"]),
        )

    def test_higher_tier_wins_for_mixed_changes(self):
        self.assertEqual(
            "high",
            classify_paths(["docs/GUI-STRATEGY.md", "FEBuilderGBA.Core/Rom.cs"]),
        )

    def test_unsafe_path_fails_closed(self):
        self.assertEqual("high", classify_paths(["../outside.txt"]))


if __name__ == "__main__":
    unittest.main()
