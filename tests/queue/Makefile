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
