# Classification and dry-run planning

## Discovery is not classification

`scan` first discovers candidate assets through the configured Immich OCR and
contextual searches. The rule engine then evaluates each asset against each
enabled category using only the search evidence recorded for that category.
Candidates that do not satisfy a category's rule remain unmatched.

The API currently returns which configured query found a candidate, not its raw
OCR document or a calibrated category confidence. Rules therefore compare
configured terms with the exact query evidence:

- `ocrAny` matches when at least one listed OCR search query returned the asset.
- `ocrAll` matches when every listed OCR search query returned the asset. This
  does **not** prove that the terms appeared together in one OCR document.
- `contextualAny` matches when at least one listed contextual/CLIP search query
  returned the asset. Search rank is not a confidence score.

Term comparison is case-insensitive and otherwise exact. It does not perform
substring matching, whitespace normalization, phrase interpretation, or
regular-expression matching. Each condition term must also be present in the
category's corresponding `search.ocr` or `search.contextual` list; configuration
validation rejects conditions the discovery stage could never evaluate.

## Rule semantics

Each condition contains exactly one operator. A rule is evaluated as follows:

1. Every condition in `all` must match.
2. If `any` contains conditions, at least one must match.
3. If `not` is present, its condition must not match.
4. No condition in `exclude` may match.

An empty `all` or `any` list adds no constraint. The configuration must still
contain a positive classification condition, so exclusions alone cannot label
an asset. A generic OCR hit such as `total`, `totaal`, or `BTW` is only evidence
that Immich returned the asset for that query; it is not proof of a receipt or
invoice.

`not` and `exclude` can only inspect positive query hits present in the
candidate's returned evidence. The absence of a hit is not proof that the term
is absent from the asset: search windows may be bounded or incomplete. Treat
negative conditions as query-result filters, not as evidence about the full OCR
document, and do not use them as the sole basis for archival rules.

Evidence is category-scoped. A hit from a category's search is not reused as
evidence for a different category, even if the query text is the same. If one
asset matches multiple categories, the default `processing.ambiguousAssetPolicy:
review` routes it to review without category actions. Setting the policy to
`skip` skips its actions instead. The engine never selects an arbitrary winner.

## Dry-run action plans

The phase-4 CLI reports unambiguous category matches and plans configured album,
tag, and archive actions. It does not execute any library mutations, regardless
of `processing.dryRun`; `--dry-run` also forces the reported dry-run setting on.
The output reports planned actions separately from executed mutations, which
remain zero. Archive plans respect `processing.maxArchiveActionsPerRun`.

Archiving is disabled in the safe example configuration. If enabling it in a
custom configuration, require multiple independent signals in the rule and
review the dry-run output first. For example:

```yaml
processing:
  dryRun: true
  ambiguousAssetPolicy: review

categories:
  - name: receipts
    enabled: true
    search:
      contextual: ["receipt", "restaurant bill"]
      ocr: ["kassabon", "btw"]
    match:
      all:
        - contextualAny: ["receipt", "restaurant bill"]
        - ocrAll: ["kassabon", "btw"]
    actions:
      album: Receipts
      archive: true
```

This example produces a plan only in the current phase. Even when both
independent search types match, the evidence is not a probability that the
asset is a receipt. Mutation execution is outside phase 4.

## Explainability and privacy

Use `scan --verbose` to see per-candidate category decisions, matched query
conditions, reasons a rule did not match, and any planned actions. Verbose output
includes Immich asset IDs; protect or omit it when sharing logs. Ordinary
summaries avoid filenames, OCR text, and asset IDs.

The summary distinguishes candidates discovered, assets evaluated, category
matches, ambiguous/unmatched assets, planned album/tag/archive actions, and
mutations executed. A completed search window is not a guarantee of exhaustive
library coverage; see [API compatibility](api-compatibility.md).
