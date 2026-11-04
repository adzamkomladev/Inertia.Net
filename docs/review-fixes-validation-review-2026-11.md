# Validation and testing reassessment (2026-11-04)

Baseline: fetched `origin/main` at `2dd4a4b` (1.6.2). This is a static source review; no phase 2 changes are implemented by this memo.

## Testing helpers (2.2)

The proposed XSRF and validation error helpers are absent. `AssertableInertia` already exposes `Status`, `HasScroll` and
`HasLive`; page parsing already supports non-success responses, and pagination reload helpers exist. Reuse those APIs.
`Inertia.Net.Testing` remains independent of the core package.

Additional regression cases to include:

- Custom cookie and header names, multiple `Set-Cookie` values, cookie URL decoding and an absent token.
- Literal dotted field keys such as `items.0.email`; validation errors are flat field-name maps, so generic dot traversal alone
  cannot resolve every key. Keep bag lookup separate from field lookup.
- Both string messages and all-errors arrays, absent fields/bags and the default empty errors object.
- XSRF header helpers preserve the antiforgery cookie sent by the test client's cookie container; a header alone cannot validate
  the token pair.

## Validation normalization (2.3)

The handler-returned `ValidationProblem` path still calls `Back` without `ToClientKeys`. MVC still selects the MVC JSON
naming policy rather than the page serializer's HTTP JSON options. The Minimal API source-generated and precognition paths
normalize without supplying the endpoint body schema.

The original phrase "JsonPropertyName is ignored everywhere" is too broad. FastEndpoints already passes its request schema to
`ToClientKeys`. `ToClientPath` can recognize an existing serialized name, including an alias, but cannot generally recover a
CLR name from a serialized alias: it only searches `JsonPropertyInfo.Name` for the input or naming-policy conversion.
`ToClientKeys` also returns early for null naming policies even when a schema is supplied.

A reliable alias implementation must preserve Native AOT. Source-generated `JsonPropertyInfo` does not guarantee an
`AttributeProvider` exposing the CLR member. Do not introduce unannotated runtime reflection or claim that merely passing
`JsonTypeInfo` fixes aliases. Choose and validate an AOT-safe mapping, or document the unsupported alias cases precisely.

Required regression coverage should include source-generated failures, returned problems, MVC ModelState and FastEndpoints
with nested collection indices; dictionary keys retain their spelling. Include null naming policy plus aliases, already-normalized
keys, duplicate errors converging to a single client key and precognition validate-only matching. Verify that the three adapter
paths emit the same client key under the same configured page serializer policy while keeping their request binding behavior.

## Generated member metadata probe

A standalone .NET 10.0.14 metadata probe on this host resolved a `[JsonPropertyName("email_address")]` property through a
source-generated context. `JsonPropertyInfo.AttributeProvider` returned `RuntimePropertyInfo`; publishing that same program
with Native AOT and `JsonSerializerIsReflectionEnabledByDefault=false` succeeded without trim warnings and returned
`NativeFormatRuntimePropertyInfo`. Therefore the built-in generated metadata supports an AOT-safe mapping using the supplied
member provider's `Name`, without adding a runtime property-discovery reflection call. Custom resolvers may omit that provider;
those must supply client JSON names for aliases that cannot be inferred. Adapter regression and repository AOT verification
still need to cover the final implementation.
