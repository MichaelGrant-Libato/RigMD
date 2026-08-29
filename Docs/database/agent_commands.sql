create table if not exists agent_commands (
    id uuid primary key,
    agent_id text not null
        references agent_devices(agent_id)
        on delete cascade,

    command_type text not null,
    status text not null default 'pending',

    requested_at timestamptz not null default now(),
    claimed_at timestamptz null,
    completed_at timestamptz null,

    error_message text null,

    constraint ck_agent_commands_type
        check (
            command_type in ('scan_system_profile')
        ),

    constraint ck_agent_commands_status
        check (
            status in (
                'pending',
                'running',
                'completed',
                'failed'
            )
        )
);

create index if not exists ix_agent_commands_agent_status
    on agent_commands (
        agent_id,
        status,
        requested_at
    );