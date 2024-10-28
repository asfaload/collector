SHELL:=/bin/bash
publish:
	for f in *.fsproj; do \
		dotnet publish $$f; \
		mv bin/Release/net8.0/publish/ bin/Release/net8.0/$${f%%.fsproj}-bin; \
		done;
		if [[ -n "$(DEST)" ]]; then mkdir -p $(DEST); mv bin/Release/net8.0/*-bin $(DEST)/; fi;
