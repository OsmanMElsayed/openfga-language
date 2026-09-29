.PHONY: build clean test lint audit format all-tests post-antlr-gen

build:
	dotnet build -c Release

clean:
	dotnet clean

test:
	dotnet test -c Release

lint:
	dotnet format --verify-no-changes
	dotnet build -c Release

audit:
	dotnet restore
	dotnet list package --vulnerable --include-transitive | tee audit.log
	! grep -q "has the following vulnerable packages" audit.log
	rm -f audit.log

format:
	dotnet format

all-tests: build audit lint test

# The ANTLR C# target always emits public types; the library exposes none of them (spec §3.4).
# Every warning in generated code is also silenced, since the code can't be edited.
post-antlr-gen:
	sed -i -E 's/^(\s*)public (partial class|class|interface) /\1internal \2 /' src/OpenFga.Language/Gen/*.cs
	sed -i '1i #pragma warning disable' src/OpenFga.Language/Gen/*.cs
