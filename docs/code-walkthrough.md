# Code walkthrough

Start with the sample flow, then trace the live path. The sample skips only the model extraction step; both paths use the same comparison code and review UI.

| Step | Where to read | What to explain |
| --- | --- | --- |
| Accept input | `src/LabelReview/Program.cs`, `LabelInput.cs` | Two named uploads, explicit sharing consent, size limit, matching extension/signature, loopback and Host checks, one live request at a time |
| Ask AI for evidence | `src/LabelReview/AiExtractor.cs` | One document per request, no tools, untrusted-document instruction, strict schema, fixed endpoint, no automatic retry, `store: false` |
| Reject bad extraction | `AiExtractor.ParseResponse`, `Comparison.Validate` | Completed response required; refusal/incomplete/malformed output rejected; all 14 fields present exactly once with consistent states |
| Compare | `Comparison.Compare` | `unclear` outranks equality; whitespace/case normalization only; changes/additions/removals remain visible; visual and textual findings separated |
| Review | `src/LabelReview/wwwroot/app.js` | Data rendered using `textContent`, original file previews, caution preserved after acknowledgment, state reset for each run |
| Export | `app.js` export handler | Content hashes, source mode, model/response IDs, extraction evidence, pending/acknowledged review state; no uploaded file bytes or API key |

The API has three routes:

- `GET /api/status`: whether live mode is configured and the selected model ID.
- `GET /api/sample`: real deterministic comparison over synthetic extraction fixtures, with hashes of the synthetic PNGs.
- `POST /api/compare`: multipart fields `before`, `after` and `consent=send`, plus header `X-Label-Review: 1`. Returns a full report only after both extractions succeed.

There is no public JSON-extraction upload route, report persistence or generic provider layer. Those features are unnecessary for this demo.

## Decisions worth discussing

1. **Keep AI responsible for reading, code responsible for comparison.** The exact comparison rule can be tested without a model. Human review remains necessary because a missing extraction can create a false negative.
2. **Use evidence states instead of confidence percentages.** A model-supplied number would not be calibrated by this project. Explicitly unreadable content is more actionable than an arbitrary score.
3. **Retain full facts-panel text.** It is the smallest implementation that preserves row amounts and units. A production system might add row-level alignment, OCR regions and source overlays after evaluation on representative labels.
4. **Make no-key mode honest.** Fixtures are manually prepared data, not model output or an accuracy benchmark. They prove the comparison/review behavior, not extraction quality.
5. **Export rather than persist.** This keeps the demo runnable without a database. It also means closing the page loses acknowledgments, and exported JSON is not a trusted approval record.

An initial evaluation you can perform with your own API account: run the two synthetic PNGs in live mode, compare every extracted field to the prepared fixtures, note missed text/uncertain reads/false differences, and repeat before changing the prompt. Do not turn a few handpicked runs into a production accuracy claim.
