; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LcdMOD004 | LcdModCodeGenerator | Warning | Client and server code must not import each other
LCDCMD001 | LcdModCodeGenerator | Error | Chat commands must be valid static methods
ADKAPI001 | AdkApiProviderGenerator | Error | API provider and manager types must be partial
ADKAPI002 | AdkApiProviderGenerator | Error | API providers must support generated GetApi members
ADKAPI003 | AdkApiProviderGenerator | Error | API methods must map to BCL Func or Action delegates
ADKAPI004 | AdkApiProviderGenerator | Error | API method ids must be unique within a provider
ADKAPI005 | AdkApiProviderGenerator | Error | API methods require an ApiProvider owner
ADKAPI006 | AdkApiProviderGenerator | Error | API managers must support generated session and registration members
ADKAPI007 | AdkApiProviderGenerator | Error | API managers require a compatible root GetApi provider
ADKAPI008 | AdkApiProviderGenerator | Error | API manager ports must be unique
ADKAPI009 | AdkApiProviderGenerator | Error | Generated API client mirror names must be valid and unique
ADKNET001 | AdkAnalyzer | Error | Network payload targets must be top-level, non-static partial classes
ADKNET002 | AdkAnalyzer | Error | Network payload IDs must be valid
ADKNET003 | AdkAnalyzer | Error | Network payload IDs must be unique
ADKNET004 | AdkAnalyzer | Error | Network callbacks must have a supported event-args or payload signature
ADKNET005 | AdkAnalyzer | Error | Network callback IDs must be valid
ADKNET006 | AdkAnalyzer | Error | Network callbacks must reference a generated payload type
