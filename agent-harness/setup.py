from setuptools import setup, find_namespace_packages

setup(
    name="cli-anything-febuildergba",
    version="1.0.0",
    description="CLI harness for FEBuilderGBA — Fire Emblem GBA ROM hacking suite",
    long_description=open("cli_anything/febuildergba/README.md").read(),
    long_description_content_type="text/markdown",
    author="laqieer",
    license="MIT",
    packages=find_namespace_packages(include=["cli_anything.*"]),
    package_data={
        "cli_anything.febuildergba": ["skills/*.md"],
    },
    install_requires=[
        "click>=8.0.0",
        "prompt-toolkit>=3.0.0",
    ],
    entry_points={
        "console_scripts": [
            "cli-anything-febuildergba=cli_anything.febuildergba.febuildergba_cli:main",
        ],
    },
    python_requires=">=3.10",
)
