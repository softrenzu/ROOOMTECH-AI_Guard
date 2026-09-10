# Security Policy

## Security model

ROOOMTECH AI Guard is designed to deny unapproved user-mode applications read access to files under administrator-configured protected folders on Windows 11.

The enforcement boundary is the Windows File System Minifilter Driver. The management GUI and Agent configure policy; they are not a substitute for the Kernel Driver.

## Current protection scope

- Protected folder allow-list enforcement at file open/read time
- Full process-image path matching in the Driver
- SHA-256 verification of administrator-registered allowed applications before policy synchronization
- Administrator-only management UI
- Kernel-mode requests are not blocked

## Out of scope / residual risks

AI Guard does not claim absolute protection against:

- a local administrator or attacker with kernel privileges;
- malicious or compromised kernel drivers;
- an approved application that itself exfiltrates plaintext;
- external camera capture of a displayed document;
- data copied outside a protected folder before protection is applied.

Clipboard, printing, screenshots, destination-aware browser/network DLP and content encryption are separate defense layers and are not represented as complete in the current release candidate.

## Production driver requirement

Only Microsoft-compliant, properly signed Driver packages using a Microsoft-assigned Minifilter Altitude may be distributed as the production Kernel component. The development Altitude in the repository must not be used for general distribution.

## Reporting a vulnerability

Do not publish exploit details or sensitive test data in a public GitHub issue. Contact ROOOMTECH株式会社 through its official business contact channel with reproduction steps, affected version, and impact.
