// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — serialize tests that swap the WindowManager singleton's navigation
// service so they don't race each other (the singleton is process-wide).
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[CollectionDefinition("WindowManagerSerial", DisableParallelization = true)]
public class WindowManagerSerialCollection { }
