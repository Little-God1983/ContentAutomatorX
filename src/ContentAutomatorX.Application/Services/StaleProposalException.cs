namespace ContentAutomatorX.Application.Services;

/// <summary>The section changed after the proposal was generated. Distinct from a general failure
/// so the UI can offer "overwrite anyway" instead of just reporting an error.</summary>
public class StaleProposalException(string message) : Exception(message);
