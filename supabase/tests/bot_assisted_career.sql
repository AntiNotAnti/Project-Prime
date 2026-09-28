-- Run after migrations. No match/participant records may be written by this call.
begin;
do $$
declare
    result jsonb;
begin
    result := public.ingest_project_prime_career_match(
        '{"version":1,"contains_bots":true}'::jsonb,
        '11111111-1111-1111-1111-111111111111'::uuid,
        0, repeat('A',64), '{}');
    if result <> '{"accepted":false,"reason":"BotAssistedMatch"}'::jsonb then
        raise exception 'Bot-assisted report reached career ingestion: %', result;
    end if;
end;
$$;
rollback;
