-- Rx | NeL 注册表
-- Supabase → SQL Editor 整段执行。依赖 Auth 的 auth.users。

create table if not exists public.rx_profiles (
    id              uuid primary key references auth.users (id) on delete cascade,
    email           text not null,
    roblox_username text not null,
    roblox_id       bigint not null,
    display_name    text,
    avatar_url      text,
    machine_id      text not null,
    created_at      timestamptz not null default now(),
    constraint rx_profiles_email_key unique (email),
    constraint rx_profiles_roblox_key unique (roblox_id),
    constraint rx_profiles_machine_key unique (machine_id)
);

alter table public.rx_profiles enable row level security;

drop policy if exists "rx_profiles_select_own" on public.rx_profiles;
drop policy if exists "rx_profiles_insert_own" on public.rx_profiles;
drop policy if exists "rx_profiles_update_own" on public.rx_profiles;

create policy "rx_profiles_select_own" on public.rx_profiles
    for select using (auth.uid() = id);

create policy "rx_profiles_insert_own" on public.rx_profiles
    for insert with check (auth.uid() = id);

create policy "rx_profiles_update_own" on public.rx_profiles
    for update using (auth.uid() = id) with check (auth.uid() = id);
