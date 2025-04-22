fixtures/checksums_toobig.txt:
	fallocate -l 1025K fixtures/checksums_toobig.txt
# Set DEBUG=1 as there's some sleep in non-debug to limit
# request rates sent to github
test: export DEBUG=1
test: fixtures/checksums_toobig.txt
	dotnet test

coverage.xml:
	# Note: last exclude filter has to be repeated
	dotnet test "/p:AltCover=true"  "/p:AltCoverAssemblyExcludeFilter='^\(FSharp.Core|FSharp|Humanizer|tests|xunit|AltCover|AltCover.Monitor|Microsoft|Octokit|NUnit|NUnit3|testhost|testhost\).*$'"

.PHONY = coverage view_coverage clean_coverage view_new_coverage

coverage: coverage.xml

coverage_report: coverage
	reportgenerator "-reports:coverage.xml" "-targetdir:coverage_report/" -reporttypes:Html

view_coverage: coverage_report
	open coverage_report/index.html

clean_coverage:
	rm coverage.xml coverage_report -rf

view_new_coverage: clean_coverage view_coverage
