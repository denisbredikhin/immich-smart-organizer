# Immich API compatibility

## Verified target and source

The initial target is Immich **3.3.5**. The versioned OpenAPI document was inspected at the `v3.3.5` tag, commit `8303cb117e197c700332b33b61b45314c9cc5bb4`:

- [Immich v3.3.5 OpenAPI specification](https://github.com/immich-app/immich/blob/v3.3.5/open-api/immich-openapi-specs.json)
- [Official Immich API reference](https://api.immich.app/)
- [Official search documentation](https://docs.immich.app/features/searching/)
- [Immich v3.2.0 release notes: Search API v2](https://immich.app/blog/v3.2.0-release)
- [v3.3.5 search controller](https://github.com/immich-app/immich/blob/v3.3.5/server/src/controllers/search.controller.ts)
- [v3.3.5 search service](https://github.com/immich-app/immich/blob/v3.3.5/server/src/services/search.service.ts)
- [v3.3.5 search repository](https://github.com/immich-app/immich/blob/v3.3.5/server/src/repositories/search.repository.ts)

The v3.2.0 release introduced Search API v2. Its OpenAPI history marks the new metadata and smart search contracts stable. This document records schema/source verification only; there has not yet been a live Immich 3.3.5 integration test.

## Authentication and permissions

The OpenAPI security scheme accepts an API key in the `x-api-key` header. The CLI should read it from the environment variable named in configuration and must never log or persist the key. The server-version endpoint is listed without an authentication requirement; search and asset endpoints accept API-key authentication.

The minimum documented permission identifiers for planned operations are:

| Operation | Endpoint | OpenAPI permission |
|---|---|---|
| Metadata/OCR search | `POST /search/metadata` | `asset.read` |
| Contextual/CLIP search | `POST /search/smart` | `asset.read` |
| Retrieve OCR text for an asset | `GET /assets/{id}/ocr` | `asset.read` |
| List albums | `GET /albums` | `album.read` |
| Create an album | `POST /albums` | `album.create` |
| Add assets to an album | `PUT /albums/{id}/assets` | `albumAsset.create` |
| List tags | `GET /tags` | `tag.read` |
| Create a tag | `POST /tags` | `tag.create` |
| Add tags to assets | `PUT /tags/{id}/assets` | `tag.asset` |
| Change an asset's visibility to archive | `PUT /assets` (bulk) | `asset.update` |

These are API permission identifiers, not a promise about every API-key role configuration. The application must fail clearly on forbidden responses and must not require or imply administrator access. API-key permission behavior still needs validation against a live server.

## OCR and contextual search are different operations

### OCR

Use `POST /search/metadata` with the Search API v2 `filter.ocr.matches` field. The `ocr` filter is a single string match expression (`StringSimilarityFilter`); it is not a list of independent terms. The older flat `MetadataSearchDto.ocr` field is marked deprecated from v3.2.0 onward.

For an asset whose recognized text is needed, `GET /assets/{id}/ocr` returns OCR records containing `text`, `textScore`, `boxScore`, and normalized box coordinates. This confirms raw recognized text is available through the official API. OCR detection/recognition scores describe text extraction, not a probability that the asset is a receipt, invoice, or any other category. Do not retain full OCR documents in SQLite or ordinary logs.

Neither the schema nor the reviewed documentation establishes exact substring, phrase, case-folding, whitespace-normalization, or multiple-term matching semantics for `filter.ocr.matches`. The application should send configured terms as separate searches, preserve the exact query associated with each hit, and avoid claiming semantics beyond observed API behavior.

### Contextual / CLIP

Use `POST /search/smart` with `query` and a `filter` object for the structured natural-language contextual search contract; send `filter: {}` when no filtering is needed. A request containing only `query` selects the legacy page-based search in v3.3.5 instead. This is separate from OCR filtering; the API does not expose a `Context` field that should be repurposed for OCR.

The response contains asset records, not a calibrated relevance score. Result ordering can be retained as server ranking, but list position must not be converted into a numeric confidence or treated as a definitive category label. The v3.3.5 service explicitly has no cursor for the new rank-aware smart-search request shape, so each query returns a bounded candidate set rather than an exhaustive result stream.

## Response shapes, pagination, and coverage

Both search operations return `SearchResponseDto`; its `assets` member contains `items`, `count`, `total`, `nextCursor`, and a legacy `nextPage` field. For the new metadata-search request shape, the v3.3.5 service accepts `cursor` and returns `nextCursor`. The implementation encodes an offset in that cursor rather than a snapshot token. Results can therefore shift while a library changes between page requests; the client must deduplicate by asset ID and must not claim snapshot consistency.

Metadata search can be paged, but the schema does not guarantee complete discovery across every asset type, library source, indexing state, or concurrent update. The filter model exposes a `libraryId`, and asset responses expose a library ID, but that alone does not establish that unrestricted search covers all external-library assets. External-library completeness and live indexing behavior remain unverified. No direct database or filesystem fallback is allowed.

Consequently:

- Treat OCR/CLIP results as query-discovered candidates, not an exhaustive library inventory.
- Report result caps, missing continuation, and search errors as partial coverage.
- `--full` must mean re-evaluation of candidates that the API can discover, not a claim that every library asset was enumerated.
- Do not use capture time alone as an incremental cursor; a newly imported historical image may have an old capture timestamp.
- Validate external-library behavior against an authorized test instance before documenting it as supported.

## Phase 3 client behavior

The CLI adds `/api` to the configured Immich server URL while preserving a reverse-proxy path (for example, `https://photos.example/immich` becomes `https://photos.example/immich/api/`). Startup calls unauthenticated `GET /server/version` before searching and accepts stable Immich 3.2.0 or later within major version 3. Search requests use the configured environment variable as `x-api-key`; the key is not printed. The default HTTP handler does not follow redirects, avoiding accidental forwarding of the key to a redirected host.

The client sends each configured OCR query separately with `filter.ocr.matches` and follows metadata `nextCursor` values. It sends each contextual query separately to smart search, whose result window has no supported continuation. Candidates are deduplicated by asset ID, and each retains its category and exact matching query as OCR or contextual evidence. The implementation does not fetch OCR documents or derive classifications or confidence scores.

Requests retry at most twice after the initial attempt for HTTP 408, 429, 5xx, network failures, and timeouts. `Retry-After` is respected up to a two-second delay. Authentication/permission failures and incompatible responses are not retried. Search failures after bounded retries, a full smart-search result window, deprecated-only metadata continuation, and configured candidate limits are reported as partial coverage. Cursor repetition is treated as an incompatible API response to prevent an unbounded loop.

`scan` reports the server version, configured scan mode, dry-run setting, unique candidate count, and any partial-coverage reasons. Exit codes are `3` for connectivity or authentication/permission errors, `4` for an unsupported API/version/response, and `5` when discovery is partial. Phase 3 is read-only: it does not classify candidates or issue album, tag, archive, or delete requests. A successful search-window pass does not imply exhaustive library coverage.

## Albums, tags, and archive

- Album display names are flat names. The API accepts an album name and returns album/user information; list and inspect existing albums before creating or mutating membership. Add assets with the album asset endpoint and reconcile state from the server response.
- Tag hierarchy is represented by `parentId` on `TagCreateDto`, not by assuming that slashes in a name create a hierarchy. Tag creation and asset assignment require separate permissions.
- The asset visibility enum includes `archive`, and the bulk asset-update payload contains `ids` and `visibility`. The v3.3.5 OpenAPI marks both `PUT /assets` and `PUT /assets/{id}` update operations deprecated; there is no non-deprecated archive mutation endpoint in the inspected schema. If archival is implemented for this target, isolate this compatibility risk, require explicit opt-in, use only the documented `visibility: "archive"` update, and verify server confirmation. Archiving remains disabled by default.
- No delete endpoint is needed for the planned operations. The tool must never call asset deletion or alter original media.

## Compatibility policy

Prioritize the verified 3.3.5 contracts and fail clearly when required endpoints, permissions, or response capabilities are unavailable. Do not add a speculative multi-version compatibility layer. Mocked tests cannot substitute for a live compatibility check; any future live test must use an authorized isolated library and begin in dry-run mode.
