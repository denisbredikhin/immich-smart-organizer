# Deployment

## Prerequisites

Install the .NET 10 runtime on the machine that runs the tool. Install the .NET 10 SDK instead if building from source. The tool is a framework-dependent .NET tool, not a self-contained executable.

The first NuGet package release has not been published yet. After publication, install globally:

```shell
dotnet tool install --global Immich.SmartOrganizer
```

Or install into a repository-local tool manifest:

```shell
dotnet new tool-manifest
dotnet tool install Immich.SmartOrganizer
dotnet tool run immich-smart-organizer -- --help
```

For development before package publication, use the commands in [README development](../README.md#development).

## Configuration and environment

Create a configuration file in the directory from which the scheduled command will run:

```shell
immich-smart-organizer init
```

Set the environment variable named by `immich.apiKeyEnv` in the process environment. Keep the key out of configuration YAML, command-line arguments, logs, and source control. Discovery needs `asset.read`; live execution also needs `user.read` and permissions for the enabled [actions](api-compatibility.md#authentication-and-permissions).

Configuration selection order is:

1. `--config PATH`
2. `IMMICH_SMART_ORGANIZER_CONFIG`
3. `immich-smart-organizer.yaml` in the current working directory

Relative configuration paths are based on the current working directory. Relative `state.path` values are based on the selected configuration file's directory. Live execution persists action progress and locks this database exclusively. Use the same canonical state path for overlapping schedules; a second live process fails rather than racing. Keep the database on a local filesystem. Dry-run does not write completed state.

The sample configuration uses `http://localhost:2283`; change it to the correct Immich base URL before scanning. Review [configuration](configuration.md) and [API compatibility](api-compatibility.md) before deploying.

## Linux scheduling

The examples below run read-only scans with `--dry-run`. Confirm the executable path and working directory for the account that owns the tool.

### systemd user timer

Store the environment file under the user account with restrictive permissions:

```shell
mkdir -p "$HOME/.config/immich-smart-organizer"
chmod 700 "$HOME/.config/immich-smart-organizer"
umask 077
install -m 600 /dev/null "$HOME/.config/immich-smart-organizer/environment"
${EDITOR:-vi} "$HOME/.config/immich-smart-organizer/environment"
chmod 600 "$HOME/.config/immich-smart-organizer/environment"
```

Put only the `IMMICH_API_KEY=...` assignment in the file; do not put the key in a shell command or commit the file.

Create `~/.config/systemd/user/immich-smart-organizer.service`:

```ini
[Unit]
Description=Immich Smart Organizer read-only scan

[Service]
Type=oneshot
WorkingDirectory=%h/immich-smart-organizer
EnvironmentFile=%h/.config/immich-smart-organizer/environment
ExecStart=%h/.dotnet/tools/immich-smart-organizer scan --dry-run
```

Create `~/.config/systemd/user/immich-smart-organizer.timer`:

```ini
[Unit]
Description=Run Immich Smart Organizer daily

[Timer]
OnCalendar=daily
Persistent=true

[Install]
WantedBy=timers.target
```

Enable the timer:

```shell
systemctl --user daemon-reload
systemctl --user enable --now immich-smart-organizer.timer
systemctl --user list-timers immich-smart-organizer.timer
```

Review output with `journalctl --user -u immich-smart-organizer.service`.

### cron

Use a private environment file and wrapper rather than putting the API key in the crontab. For example, save this as `~/.local/bin/run-immich-smart-organizer`:

```sh
#!/bin/sh
set -a
. "$HOME/.config/immich-smart-organizer/environment"
set +a
cd "$HOME/immich-smart-organizer" || exit 1
exec "$HOME/.dotnet/tools/immich-smart-organizer" scan --dry-run
```

Restrict the environment file to its owner (`chmod 600`), ensure it contains only the intended variable assignment, then make the wrapper executable (`chmod 700`). Add a schedule such as this with `crontab -e`:

```cron
0 2 * * * /home/USER/.local/bin/run-immich-smart-organizer >> /home/USER/.local/state/immich-smart-organizer.log 2>&1
```

Replace `USER` with the account name and ensure the log directory exists with appropriate permissions.

## Windows Task Scheduler

1. Install the global tool for the account that will own the scheduled task.
2. Set `IMMICH_API_KEY` in that account's user environment, or use an approved secret-management mechanism to provide it to the process.
3. Create a task with the same account and appropriate stored-credential settings.
4. Set **Program/script** to `%USERPROFILE%\.dotnet\tools\immich-smart-organizer.exe`.
5. Set **Add arguments** to `scan --dry-run`.
6. Set **Start in** to the directory containing `immich-smart-organizer.yaml`.
7. Configure the schedule and review the task's last run result and output.

Do not put the API key in the task's arguments or a checked-in script.

## Updates and removal

Update a global installation:

```shell
dotnet tool update --global Immich.SmartOrganizer
```

Update a local tool from its manifest directory:

```shell
dotnet tool update Immich.SmartOrganizer
```

Uninstall globally with `dotnet tool uninstall --global Immich.SmartOrganizer`; uninstall locally with `dotnet tool uninstall Immich.SmartOrganizer`. Remove the configuration and state files separately if they are no longer needed, taking care not to remove any unrelated user data.

Scheduling is external to the tool. There is no built-in scheduler or background service.
