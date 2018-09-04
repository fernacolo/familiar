
# Features

- LRU search.
- Distributed DB.

# Concepts

## Session

The program is used in sessions. A session is unically identified by machine, user and code. The code is a random value assigned by the program. The session survives crashes and machine reboots. When the program is used for the first time, a session is created.

Each session has a probabilistic unique session id, referred as `sessionid`. When a session is created, the `sessionid` is computed as a hash from the string `{host-name}/{user-name}/{code}`.

## Synchronization

All commands executed in a session are pushed to a remote location, and all commands from remote location are pulled to the local session. This happens automatically in the background. The user feels like the program uses a distributed database.

When the program is used fo the first time, the user selects the remote location for synchronization. The remote location is a shared file system. It does not belong to any specific session.

### Update and Sharing Logic

- After the typed command is executed, it's immediatelly written in the session.
- Periodically, local changes in the session are pushed to remote.
- Periodically, changes from other sessions, already present at remote location, are pulled to the session.

## Persisted Data Storage

### Local Session Data

The local session, used for every desktop process, is stored at `%USERPROFILE%\ocmd`. The following files are stored there:

- `config.properties` - A properties file with configuration information.
- `{sessionid}.sesdef` - The session definition file.
- `{sessionid}-{nnnn}.dat` - Session data file in "closed" state. `{nnnn}` is a number from `0000` to `FFFF`.
- `{sessionid}-last.dat` - Session data file in "open" state.
- Index files (TODO)

### Remote Session Data

A shared directory in some remote file system holds session data for different machines and users. This shared directory is specified by the user at first execution, and stored in the `config.ini` file.

The program supports delayed synchronization, which allows most users to specify a cloud storage location such as OneDrive, DropBox, etc. It also supports variable expansion. For OneDrive users, it's practical to specify `%OneDrive%\ocmd`.

The following files are periodically copied from local session to the shared directory:

- `{sessionid}.properties` - A copy of the `config.properties` file of the session identified by `{sessionid}`.
- `{sessionid}.sesdef` - Session definition file.
- `{sessionid}-{nnnn}.dat` - Session data file in "closed" state. `{nnnn}` is a number from `0000` to `FFFF`.
- `{sessionid}-last.dat` - Session data file in "open" state.

Because `{sessionid}` unically identifies each session, the shared directory can store data from all sessions.

### Synchronization

Periodically, any active process can copy local session files to the designated remote directory.

## File Formats

### Session Definition File (.sesdef)

Session Data File (.dat)

A sequence of records, where each contains the following format:












Search:

-Simply read local DB. Search commands in reverse date order (youngest commands appear first).
