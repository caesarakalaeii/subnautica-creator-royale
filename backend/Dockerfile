# syntax=docker/dockerfile:1

FROM golang:1.26-alpine AS build
WORKDIR /src

# The service is standard-library only, so go.sum may not exist; the glob tolerates either case.
COPY go.mod go.su[m] ./
RUN go mod download

# The commit stamped into the binary, so the page can link the exact source being served (AGPL §13).
# Passed as a build argument because the build context has no .git, so vcs.revision is never set.
# Empty is allowed: the page then reports the revision as unknown.
ARG REVISION=""

COPY . .
RUN CGO_ENABLED=0 go build -trimpath \
  -ldflags="-s -w -X github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/caster.revision=${REVISION}" \
  -o /out/caster ./cmd/caster

# The soak tool ships in the same image so it always measures the build that is serving.
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/soak ./cmd/soak

# Distroless: no shell, no package manager, non-root.
FROM gcr.io/distroless/static-debian13:nonroot
COPY --from=build /out/caster /caster
COPY --from=build /out/soak /soak

# The soak's payload is docs/telemetry/example.json, staged beside the Dockerfile by CI. The glob
# keeps a plain `docker build ./backend` working without it.
COPY telemetry-exampl[e].json /telemetry-example.json
EXPOSE 8080
USER nonroot:nonroot
# No HEALTHCHECK: there is no shell to run one; probe /healthz instead.
ENTRYPOINT ["/caster"]
