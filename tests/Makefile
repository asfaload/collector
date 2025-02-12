SHELL := /bin/bash

test-%:
		$(MAKE) -C "$*" test;

test: test-collectChecksums  test-generateIndex  test-githubNotifications  test-queue)
