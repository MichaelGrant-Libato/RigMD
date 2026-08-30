alter table agent_snapshots
add column if not exists command_id uuid null;

alter table agent_snapshots
add constraint fk_agent_snapshots_command
foreign key (command_id)
references agent_commands(id)
on delete set null;

create index if not exists ix_agent_snapshots_agent_command
on agent_snapshots (
    agent_id,
    command_id
);