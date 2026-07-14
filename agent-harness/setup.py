import os
from setuptools import setup, find_namespace_packages

here = os.path.dirname(os.path.abspath(__file__))
readme_path = os.path.join(here, "cli_anything", "febuildergba", "README.md")
try:
    with open(readme_path, encoding="utf-8") as f:
        long_description = f.read()
except FileNotFoundError:
    long_description = "CLI harness for FEBuilderGBA"

setup(
    name="cli-anything-febuildergba",
    version="1.0.0",
    description="CLI harness for FEBuilderGBA — Fire Emblem GBA ROM hacking suite",
    long_description=long_description,
    long_description_content_type="text/markdown",
    author="laqieer",
    license="MIT",
    packages=find_namespace_packages(
        include=["cli_anything.*"],
        exclude=["cli_anything.*.tests", "cli_anything.*.tests.*"],
    ),
    package_data={
        "cli_anything.febuildergba": ["skills/*.md"],
    },
    install_requires=[
        "click>=8.0.0",
        "prompt-toolkit>=3.0.0",
    ],
    extras_require={
        # The MCP server itself is dependency-free stdlib JSON-RPC; this
        # extra only bounds the test runner used by CI/local development.
        "test": ["pytest>=8,<9"],
    },
    entry_points={
        "console_scripts": [
            "cli-anything-febuildergba=cli_anything.febuildergba.febuildergba_cli:main",
            "cli-anything-febuildergba-mcp=cli_anything.febuildergba.mcp_server:main",
        ],
    },
    python_requires=">=3.10",
)
