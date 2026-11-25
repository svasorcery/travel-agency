# Trips local verification

## MODIFIED Requirements

### Requirement: Explicit isolated stack lifecycle
The verification tooling SHALL start only explicitly requested, owned local resources with fictional identities, separate data/keys and loopback ingress. It SHALL preserve data on ordinary stop/restart and SHALL refuse foreign state, resources, remote daemons and occupied ports before mutation.

#### Scenario: Fresh start
- **WHEN** the developer explicitly starts a fresh local demo with prerequisites installed
- **THEN** the real services and ordinary browser UI become ready using newly provisioned fictional users and keys, and safe access instructions identify the local URLs and private credential file.

#### Scenario: Resume and stop
- **WHEN** the owner stops and later starts the same valid demo state
- **THEN** the same database, users and protection keys remain in use and saved plans remain readable.

#### Scenario: Foreign target or port collision
- **WHEN** state ownership, daemon identity, resource labels or required port availability do not match
- **THEN** the command fails without changing that state, deleting resources or stopping another service.

#### Scenario: Explicit small subnets for a fresh ordinary demo
- **WHEN** the developer explicitly runs `up --small-subnets` for a fresh ordinary demo on a compatible local Docker Engine
- **THEN** the stack requests two separate automatically allocated /24 networks, preserving the ordinary Host configuration, wall clock, default ports and ownership checks. Plain `up` for a fresh demo still uses the default Docker network allocation.

#### Scenario: Resume a saved network selection
- **WHEN** ordinary demo state already exists
- **THEN** plain `up` uses its recorded topology. Passing `--small-subnets` again is refused before any resource or state mutation.
