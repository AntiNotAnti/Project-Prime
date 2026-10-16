# CINF/CSKR malformed import count hardening

A source-provided `uint32` count must be compared with the parser's own
budget **before** converting to signed `int`. Previously `uint.MaxValue`
triggered an unchecked exception class instead of the deliberately handled
`InvalidDataException`, failing the import acceptance gate.

This corrects the CINF skeleton count and every CSKR group, influence and
vertex count. CSKR now explicitly checks buffer bounds before reading each
group's weights and gives one structured data error for truncated sources.

Regression fixtures cover oversized skeletons, groups, weights, vertices,
and truncated CSKR headers, without requiring proprietary game assets.
