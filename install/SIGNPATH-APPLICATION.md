# SignPath.io application -- Dictionary Tasker

Goal: replace the self-signed cert with a **publicly-trusted code signing
certificate** via SignPath's free OSS program. Once approved, Windows
SmartScreen stops warning end-users and the "Unknown Publisher" label
goes away -- no more "run anyway" click-through.

## Eligibility (quick check)

- [x] Public GitHub repository -- https://github.com/robertorenz/clarionDictionaryTasker
- [x] OSI-approved license -- MIT (`LICENSE`)
- [x] Real commit history (not a stub project)
- [x] README explains what the project does
- [ ] At least a few GitHub stars / activity -- nice-to-have, not mandatory

Looks approvable. They reject projects that are clearly personal-only or
that haven't shipped anything; a working installer with a changelog and
docs puts you well past that bar.

## Application steps (you do these)

Note the two domains -- they're separate things:
- **signpath.org** -- the *Foundation* (non-profit). This is who
  sponsors free code signing for OSS projects.
- **signpath.io** -- the *commercial* product. Same company, but paid.

You want the Foundation's application form.

1. **Go to the Foundation apply page:** https://signpath.org/apply
   (or start at https://signpath.org/ and click **Apply** in the top
   navigation -- same destination). The page title reads "Apply for a
   free SignPath.io subscription" which is confusing but correct: the
   Foundation sponsors free access to the .io product.

2. **Fill in the form.** Have this ready to paste:
   - **Project name:** Dictionary Tasker
   - **Repository URL:** https://github.com/robertorenz/clarionDictionaryTasker
   - **License:** MIT (OSI-approved)
   - **Contact email:** roberto.renz@reddinassessments.com
   - **Description:** Clarion IDE add-in that adds dictionary maintenance,
     linting, comparison, SQL DDL generation, and batch editing tools to
     Clarion 10, 11, 11.1, and 12. Distributed as a signed Inno Setup
     installer.
   - **Why signing matters:** End-users are Clarion developers installing
     a DLL into their IDE. Unsigned installers trip SmartScreen and make
     non-technical users hesitate; a trusted signature removes friction
     and lets the IDE's add-in loader verify provenance.
   - **Build reproducibility:** Inno Setup compiles `install/setup.iss`
     against `ClarionDctAddin\bin\Debug\ClarionDctAddin.dll`; the build
     is scripted (`install/build-installer.bat`) and can be reproduced
     in CI with the Windows .NET Framework 4.0 targeting pack + Inno
     Setup 6.

3. **Accept their terms.** Key constraints to be aware of before you
   submit (these come from https://signpath.org/terms.html):
   - No malware / PUPs.
   - OSI license without commercial dual-licensing.
   - No proprietary non-OSS components bundled (system libs are fine).
   - Must be actively maintained and already released in the form to be
     signed.
   - Every release requires manual approval in their dashboard before
     signing -- no fully unattended CI signing.

4. **Wait for review.** Typically a few business days. They sanity-check
   the repo (real project, OSI license, no malware indicators, active
   commits).

5. **Once approved:**
   - Create a **Project** in SignPath tied to this repo.
   - Create a **Signing Policy** (pick "release-signing" -- used for
     tagged releases).
   - Generate a **CI Token** -- this is what the GitHub Actions workflow
     will authenticate with. Save it as a repo secret named
     `SIGNPATH_API_TOKEN`.
   - Note the **Organization ID**, **Project slug**, and **Signing
     Policy slug** -- these go into the workflow file.

## What I'll wire up once you're approved

When you have the org/project slugs + API token in hand, I'll add:

- `.github/workflows/sign-release.yml` -- on push of a `v*` tag, it
  builds the installer, uploads the artifact, calls SignPath's
  `submit-signing-request` action, and re-downloads the signed exe
  ready for the GitHub Release.
- `install/build-installer.bat` changes so local builds still use the
  self-signed cert (dev loop stays fast) but CI release builds use
  SignPath.

Until then, the self-signed path handles the "signed, tamper-evident"
requirement -- just without the pre-trusted chain.

## Timeline / fallback

- Application review: few days to ~2 weeks
- If rejected or too slow: Certum Open Source Code Signing (~USD $25/yr)
  is the cheapest alternative with a real CA chain; no application needed.
