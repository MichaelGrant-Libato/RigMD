ALTER TABLE agent_commands
DROP CONSTRAINT IF EXISTS ck_agent_commands_type;

ALTER TABLE agent_commands
ADD CONSTRAINT ck_agent_commands_type
CHECK
(
    command_type IN
    (
        'scan_system_profile',
        'clear_user_temp_files',
        'flush_dns'
    )
);