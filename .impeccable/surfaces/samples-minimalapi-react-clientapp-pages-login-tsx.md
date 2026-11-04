---
version: 1
slug: "samples-minimalapi-react-clientapp-pages-login-tsx"
primary_target: "samples/MinimalApi.React/ClientApp/pages/Login.tsx"
related_targets: ["samples/MinimalApi.React/ClientApp/Layout.tsx","samples/MinimalApi.React/ClientApp/pages/Error.tsx"]
---

# Sample login validation

Mode: Operate. Audience: developers trying Precognition in the React sample. Route: `/login`.
This is a specified addition to the existing sample, with its visual language preserved. Use dummy values; no identity session is created.

## Direction contract

THESIS: Show blur validation without consuming the real-submit rate bucket. Keep the two-field task and its feedback visible together.

OWN-WORLD: Inherit the sample's white canvas, sans-serif text, native inputs and buttons, simple link navigation and centered 640px column. Use normal document flow and visible native focus.

STORY: Read the demo note, enter an email and password, leave a field to validate, then submit. Validation errors identify their field; accepted submissions update the count; a throttled submit gives a clear retry page.

FIRST VIEWPORT: Existing header/navigation, a Login validation heading, a short dummy-value note, stacked labeled email/password fields, checking/error feedback, Submit demo action and accepted-submission count. Fields fit a 390px viewport and navigation wraps at narrow widths.

FORM: Existing sample form extension; seed key not applicable (precisely specified narrow addition, no concept roll). No raster assets or new visual system.

FINISH: ship. The default, field-validation and 429 states were reviewed on desktop and mobile against the incumbent sample captures. No material fixes or raster assets are required.

## Verified implementation

`Layout.tsx` retains the existing sans-serif family and centered content column (maximum width 640px, padding 16px). Navigation uses wrapping flex layout with 12px gaps and 8px vertical margins. The canvas, text, links and controls retain the sample's browser-native appearance; no new palette, font, shadow or motion system was introduced.

`Login.tsx` limits the form to 360px and lets its inputs fill the available width. Labels sit above their fields with a 4px gap; field groups have 16px bottom spacing. Inputs inherit the surrounding font, use 6px padding and have a 40px minimum height. The native submit button inherits the font, has the same minimum height and uses 6px by 12px padding. Browser focus remains visible.

Field errors have alert semantics and are associated with their inputs. Checking feedback uses a polite live status, while submission disables the action and changes its label. Password guidance remains adjacent to its field. The page explicitly requests dummy values and says that it does not sign users in. The accepted-submission count stays beneath the form; the shared error page presents the 429 retry message within the same layout.

## Finish review

Disposition: **ship**. All six login captures show the expected default, validation and 429 content at desktop (1440 × 900) and mobile (390 × 844), without clipping. The two incumbent contact-page captures establish the inherited native appearance. The supplied detector report contains no findings.

This is an extension of the existing sample rather than a new visual world. The root `DESIGN.md` describes the docs site and remains outside this surface's scope; its sidecar was not regenerated. No raster assets were introduced, so no asset provenance entries are needed.
