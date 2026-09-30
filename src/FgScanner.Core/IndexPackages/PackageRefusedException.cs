namespace FgScanner.Core.IndexPackages;

/// <summary>
/// A package the reader will not open, with the operator-facing reason.
/// A refusal always happens BEFORE any document is shown: a damaged or
/// unknown package that opened anyway would put wrong pages in front of
/// Jim, and his answers would land on a register as real decisions.
/// </summary>
public sealed class PackageRefusedException(string message) : Exception(message);
