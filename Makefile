#----------------------------------------------------------------------------------------------------------------------
#
# CONFIGURATION
#
#----------------------------------------------------------------------------------------------------------------------

DOCKER_IMAGE_VERSION=3.1
DOCKER_IMAGE:=mcr.microsoft.com/dotnet/core/sdk:${DOCKER_IMAGE_VERSION}
ARCH:=x64
PROJ_DIR:=$(shell pwd)
UID=$(shell id -u)
GID=$(shell id -g)

.DEFAULT_GOAL=all
SHELL:=bash
MAKEFLAGS:=
DOCKER_FLAGS:= -it --rm -v ${PROJ_DIR}:/source

SPLIT ?= 0
CONF:=publish
CONF_FLAGS:=publish

#----------------------------------------------------------------------------------------------------------------------
#
# PRINTING
#
#----------------------------------------------------------------------------------------------------------------------
COM_COLOR   = \033[0;34m
OBJ_COLOR   = \033[0;36m
OK_COLOR    = \033[0;32m
ERROR_COLOR = \033[0;31m
WARN_COLOR  = \033[0;33m
NO_COLOR    = \033[m

OK_STRING    = "[OK]"
ERROR_STRING = "[ERROR]"
WARN_STRING  = "[WARNING]"
COM_STRING   = "Compiling"

#----------------------------------------------------------------------------------------------------------------------
#
# VERBOSITY
#
#----------------------------------------------------------------------------------------------------------------------

# Levels of V can increase verbosity of 'make'. This can be made your global default by setting MAKEVERBOSE in your shell.
#
# The meanings of the various verbosity levels are:
#
# V=0: Only show the summary and errors during a C# compliation
# V=1: Instead of suppressing warnings, add in warnings as well
# V=2: Stricter logging that should be similar to 1, but makes sure to add errors
MAKEVERBOSE ?= 0
V ?= ${MAKEVERBOSE}

define DEBUGMAKE
MAKEFLAGS+=--debug=$1
SHELL := bash $2
endef

# Helper for handling C# DotNet flags similar to C with Wextra,Wall, etc. Run with V=<X> on command line to change
# the logging level. Such as "make V=1" Do not tab, these ifeqs need to be same line due to evals.
ifeq (${V},0)
.SILENT:
BUILD_FLAGS:=ErrorsOnly;Summary
else ifeq (${V},1)
$(eval $(call DEBUGMAKE,b))
BUILD_FLAGS:=Summary;Warnings
else ifeq (${V},2)
$(eval $(call DEBUGMAKE,bv))
BUILD_FLAGS:=Summary;Warnings;Errors
else ifeq (${V},3)
$(eval $(call DEBUGMAKE,bv,-x))
BUILD_FLAGS:=Summary;Warnings;Errors
else
$(error Unsupported Verbosity Level=${V})
endif

# Upcoming flag to allow changing the target direct from the original barotrauma/bin
ifeq (${SPLIT},0)
else ifeq (${SPLIT},1)
SPLIT:=1
else
$(error Unsupported Split Flag=${SPLIT})
endif

# Control specifics on build, these can run before the preprocessing of the lower eval template. Use CONF=<X> to trigger,
# default will always be publish unless specified.
# Build: Only creates executables
# Publish: Create everything
ifeq (${CONF},build)
else ifeq ($(CONF),Build)
	CONF_FLAGS:=build
	EXTRA_FLAGS:=
else ifeq ($(CONF),publish)
	EXTRA_FLAGS:=--self-contained
else ifeq ($(CONF),Publish)
	EXTRA_FLAGS:=--self-contained
else
	$(error Unsupported Configuration Level=${CONF})
endif

#----------------------------------------------------------------------------------------------------------------------
#
# OS
#
#----------------------------------------------------------------------------------------------------------------------

OS_ARR=linux mac windows

.PHONY: all
all: $(filter-out base,${OS_ARR})

#----------------------------------------------------------------------------------------------------------------------
#
# PREBUILD
#
#----------------------------------------------------------------------------------------------------------------------

.PHONY: prebuild
prebuild:
	@printf "%b" "\n${OK_COLOR}Barotrauma Container Build System\n"
	@printf "%b" "${COM_COLOR}Image:${NO_COLOR}${OBJ_COLOR} ${DOCKER_IMAGE}${NO_COLOR}\n"
	@printf "%b" "${COM_COLOR}Arch:${NO_COLOR}${OBJ_COLOR} ${ARCH}${NO_COLOR}\n"
	@printf "%b" "${COM_COLOR}ProjectDir:${NO_COLOR}${OBJ_COLOR} ${PROJ_DIR}${NO_COLOR}\n\n"

#----------------------------------------------------------------------------------------------------------------------
#
# HELPERS
#
#----------------------------------------------------------------------------------------------------------------------

.PHONY: shell
shell: prebuild
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL}

.PHONY: purge clean
purge clean:
	@rm -rf "${PROJ_DIR}/Barotrauma/bin"

.PHONY: help list targets
help list targets:
	@$(MAKE) -pRrq -f $(lastword $(MAKEFILE_LIST)) : 2>/dev/null | awk -v RS= -F: '/^# File/,/^# Finished Make data base/ {if ($$1 !~ "^[#.]") {print $$1}}' | sort | egrep -v -e '^[^[:alnum:]]' -e '^$@$$'

#----------------------------------------------------------------------------------------------------------------------
#
# DOCKER TEMPLATE
#
#----------------------------------------------------------------------------------------------------------------------

define OS_TEMPLATE

.PHONY: $1 $1-release
$1 $1-release: $1-server $1-client

.PHONY: $1-debug
$1-debug: $1-server-debug $1-client-debug

.PHONY: $1-server $1-server-release
$1-server $1-server-release: prebuild
	@printf "%b" "\n${COM_COLOR}${COM_STRING}:${NO_COLOR}${OBJ_COLOR} ${1}-server${NO_COLOR}\n\n"
	docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "cd /source/Barotrauma/BarotraumaServer; dotnet ${CONF_FLAGS} $(shell echo $1 | sed 's/./\U&/')Server.csproj -c Release -clp:\"${BUILD_FLAGS}\" ${EXTRA_FLAGS} -r $2-x64 \/p:Platform=\"${ARCH}\""
	docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "chown -R ${UID}:${GID} /source/Barotrauma/bin"

.PHONY: $1-server-debug
$1-server-debug: prebuild
	@printf "%b" "\n${COM_COLOR}${COM_STRING}:${NO_COLOR}${OBJ_COLOR} ${1}-server-debug${NO_COLOR}\n\n"
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "cd /source/Barotrauma/BarotraumaServer; dotnet ${CONF_FLAGS} $(shell echo $1 | sed 's/./\U&/')Server.csproj -c Debug -clp:\"${BUILD_FLAGS}\" ${EXTRA_FLAGS} -r $2-x64 \/p:Platform=\"${ARCH}\""
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "chown -R ${UID}:${GID} /source/Barotrauma/bin"

.PHONY: $1-client
$1-client: prebuild
	@printf "%b" "\n${COM_COLOR}${COM_STRING}:${NO_COLOR}${OBJ_COLOR} ${1}-client${NO_COLOR}\n\n"
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "cd /source/Barotrauma/BarotraumaClient; dotnet ${CONF_FLAGS} $(shell echo $1 | sed 's/./\U&/')Client.csproj -c Release -clp:\"${BUILD_FLAGS}\" ${EXTRA_FLAGS} -r $2-x64 \/p:Platform=\"${ARCH}\""
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "chown -R ${UID}:${GID} /source/Barotrauma/bin"

.PHONY: $1-client-debug
$1-client-debug: prebuild
	@printf "%b" "\n${COM_COLOR}${COM_STRING}:${NO_COLOR}${OBJ_COLOR} ${1}-client-debug${NO_COLOR}\n\n"
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "cd /source/Barotrauma/BarotraumaClient; dotnet ${CONF_FLAGS} $(shell echo $1 | sed 's/./\U&/')Client.csproj -c Debug -clp:\"${BUILD_FLAGS}\" ${EXTRA_FLAGS} -r $2-x64 \/p:Platform=\"${ARCH}\""
	@docker run ${DOCKER_FLAGS} ${DOCKER_IMAGE} ${SHELL} -c "chown -R ${UID}:${GID} /source/Barotrauma/bin"

.PHONY: ${1}-clean
${1}-clean:
	@rm -rf "${PROJ_DIR}/Barotrauma/bin/"*$1
endef

#----------------------------------------------------------------------------------------------------------------------
#
# GENERATE DOCKER
#
#----------------------------------------------------------------------------------------------------------------------
$(eval $(call OS_TEMPLATE,linux,linux))
$(eval $(call OS_TEMPLATE,mac,osx))
$(eval $(call OS_TEMPLATE,windows,win))

