module github.com/victorcondackdev/brpay-dotnet/tools/vecgen

go 1.22

// The generator runs against a local checkout of the Go implementation,
// cloned next to this repository (../brpay relative to the repository root).
replace github.com/victorcondackdev/brpay => ../../../brpay

require github.com/victorcondackdev/brpay v0.0.0-00010101000000-000000000000
