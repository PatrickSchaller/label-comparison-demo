# Validation record

Local validation on October 28, 2026, Windows, .NET SDK 10.0.101, Node.js 22.18.0, Chromium through Playwright 1.64.1.

- Release compilation succeeds with warnings treated as errors.
- 39 .NET tests pass: normalization, changed/added/removed/unclear states, strict extraction validation, refusals/incomplete responses, request protocol, size boundaries, provider errors and content hashing.
- Endpoint integration tests use ASP.NET's in-process test host and a simulated HTTP provider. They exercise both uploaded labels through extraction and comparison, reject invalid consent/files before provider use, and verify that a failed second request releases the concurrency slot without returning a partial report.
- Four Chromium tests pass: the sample review/export/reset workflow, 390px mobile layout and inert handling of injected markup, live-error recovery, and local-server request protections.
- Synthetic sample: 14 fields, 9 findings, 1 of them unclear, 5 unchanged. Acknowledging the unclear field does not remove its caution from exported results.
- Source labels and extraction fixtures are authored together by `tools/generate-samples.mjs`. They are synthetic, not scraped customer data or actual AI recordings.

This machine's .NET HTTPS trust environment could not restore from NuGet directly. Local checks restored the same pinned public test packages from an existing local feed; hosted GitHub checks use ordinary NuGet restore. The application itself has no package dependencies beyond the .NET shared framework.

Not checked with a paid AI account. No employer API keys or labels were used. Protocol tests verify request construction and response handling; they cannot establish actual provider access, current model compatibility, OCR accuracy, visual-difference quality or production performance. The README gives the live configuration and a first-run evaluation procedure.

Hosted Windows/Linux build, test, publish and Linux browser results are available on the repository's Actions page. A successful workflow verifies the checkout compiles; it does not deploy a public web service.
