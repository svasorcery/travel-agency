# Base-codec compatibility fixtures

The two fictional, canonical detail bodies were produced by a standalone writer
compiled exclusively from eight blobs of base
`11ba5a1572e53cd901ae4ec56b77b482ec95f699`, with cached ErrorOr2.0.1/net10.0.
There is no ProjectReference to current code. `provenance.json` binds every base
blob/SHA256, writer/project/DLL and output hash; writer sources are retained as text.

They preserve catalogue1, historical city display names, block order, Unicode and
newlines, exact decimal strings, local times and null estimates. Do not reformat
the detail files or append a newline: they are exact canonical codec output.

Generation occurred after implementation began, from the exact preserved base
blobs. This is explicit old-writer evidence, not a claim that these files existed
before edits. The M1 codec/domain closure itself remains unchanged from that base.

Reproduce in a new task-owned directory: extract the eight listed blobs using
`git show BASE:PATH` without text transcoding, copy the retained writer/project
text to Program.cs/LegacyCodecWriter.csproj, and restore/build with
`-p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
-p:ManagePackageVersionsCentrally=false`. Restore against a new empty local feed
with the existing package cache; run the compiled DLL with a new absolute output
directory argument. Compare hashes with provenance; never overwrite these fixtures.

Current .NET and TypeScript readers verify the exact bodies. Protection and Host
tests encrypt these old plaintext bodies using disposable current-runtime keys.
They test binding and preservation; they do not claim to contain ciphertext or
keys produced by an old deployment. No user demo or private production data was read.
