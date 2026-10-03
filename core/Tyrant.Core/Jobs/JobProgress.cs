namespace Tyrant.Core.Jobs;

/// <summary>Progress of a long-running job; Fraction is 0..1.</summary>
public readonly record struct JobProgress(double Fraction, string Message);
