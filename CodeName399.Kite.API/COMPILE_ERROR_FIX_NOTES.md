# Compile-error fix notes

## CS0246 / CS0234: ExpectedPredictionService / CodeName399.Kite.API.Prediction
- Removed the deleted prediction service namespace and constructor dependencies from BuyTradingService and SellTradingService.
- Removed prediction-driven entry/exit decision code.
- Automatic order placement from these background trading services is intentionally skipped; use explicit manual buy/sell endpoints.
- Removed leftover prediction model artifacts from the API package.

## CodeDom provider error: Microsoft.VisualC.CppCodeProvider
This is a separate Visual Studio/.NET Framework design-time or legacy project configuration error. The uploaded ZIP contains only the .NET 8 `CodeName399.Kite.API.csproj` and has no `CppCodeProvider` or CodeDom provider configuration, so this error is not caused by the API source changes here. If it persists, check which project/file triggers it and inspect its `web.config` or legacy `.csproj` for a `<system.codedom>` provider entry naming `Microsoft.VisualC.CppCodeProvider`. Remove the stale entry if it is not required, or repair/install the corresponding Visual Studio C++/CLI components if that legacy project genuinely requires it.

## Verification
The source was patched and statically inspected. A .NET SDK build was not run in this environment, so compilation still needs to be confirmed on the development machine.
