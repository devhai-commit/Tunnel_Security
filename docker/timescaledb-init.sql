-- TimescaleDB initialization for Tunnel Security
-- This script runs automatically on first container start.

CREATE DATABASE tunnel_timeseries;

\connect tunnel_timeseries;

CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;
