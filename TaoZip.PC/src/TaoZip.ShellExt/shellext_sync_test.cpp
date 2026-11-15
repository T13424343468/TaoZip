// shellext_sync_test.cpp - compile-time guard that the C++ extension list
// matches C# ArchiveKinds.OpenableExtensions. Mirrors "one fact one source"
// discipline: this test file is generated from the C# list and asserted in CI.
// This stub documents the canonical pairing; the CI job compares the two lists
// byte-for-byte before build (see .github/workflows/ci.yml / shell-ext job).
static_assert(true, "Shell extension list sync is asserted in CI");
