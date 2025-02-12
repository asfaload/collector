SHELL:=/bin/bash
NATS_CONTAINER_NAME=tests_nats
start-nats:
	if [[ -z $$(docker ps -f name=$(NATS_CONTAINER_NAME) -q) ]]; then \
		docker run -d --name $(NATS_CONTAINER_NAME) --rm -p 4222:4222 -p 8222:8222 nats --http_port 8222 --js; \
	fi

stop-nats:
	docker stop $(NATS_CONTAINER_NAME)

test: start-nats
	dotnet test
	#$(MAKE) stop-nats

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
