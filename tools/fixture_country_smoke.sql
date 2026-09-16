-- Minimal data for the end-to-end MQTT smoke test, not the demo seed (#15).
-- Without it the ingestion consumer drops every reading (unknown sensor or no
-- assigned batch) and the test would pass while writing nothing.
--
-- Idempotent: safe to replay on a populated database.

BEGIN;

-- Brazilian band from the brief (29 °C ± 3, 55 % ± 2), the same one head office
-- seeds. Matches the simulator defaults: nominal sensors stay in, drifting ones leave.
INSERT INTO countries (country_id, country_name, country_code,
                       nominal_temp, tolerance_temp, nominal_humidity, tolerance_humidity)
VALUES ('11111111-1111-1111-1111-111111111111', 'Brésil', 'BR', 29, 3, 55, 2)
ON CONFLICT DO NOTHING;

-- Head office's DevelopmentSeeder references are the source of truth: a pushed
-- alert or batch naming one we lack gets a 404 or a 422. DO UPDATE rather than
-- DO NOTHING, so replaying the fixture also fixes databases seeded with older refs.
INSERT INTO warehouses (warehouse_id, country_id, warehouse_name, warehouse_ref)
VALUES
  ('22222222-2222-2222-2222-222222222222',
   '11111111-1111-1111-1111-111111111111', 'Entrepôt Santos', 'WH-BR-SANTOS'),
  ('22222222-2222-2222-2222-222222222223',
   '11111111-1111-1111-1111-111111111111', 'Entrepôt Cerrado', 'WH-BR-CERRADO')
ON CONFLICT (warehouse_id) DO UPDATE
  SET warehouse_name = EXCLUDED.warehouse_name, warehouse_ref = EXCLUDED.warehouse_ref;

INSERT INTO farms (farm_id, country_id, farm_name, farm_ref)
VALUES
  ('33333333-3333-3333-3333-333333333333',
   '11111111-1111-1111-1111-111111111111', 'Fazenda Boa Vista', 'FARM-BR-BOAVISTA'),
  ('33333333-3333-3333-3333-333333333334',
   '11111111-1111-1111-1111-111111111111', 'Fazenda Serra Alta', 'FARM-BR-SERRAALTA'),
  ('33333333-3333-3333-3333-333333333335',
   '11111111-1111-1111-1111-111111111111', 'Fazenda Rio Verde', 'FARM-BR-RIOVERDE')
ON CONFLICT (farm_id) DO UPDATE
  SET farm_name = EXCLUDED.farm_name, farm_ref = EXCLUDED.farm_ref;

-- A control batch that stays compliant, and one that must flip.
INSERT INTO batches (batch_id, warehouse_id, farm_id, batch_ref, stored_at, batch_status, is_compliant)
VALUES
  ('44444444-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222',
   '33333333-3333-3333-3333-333333333333', 'BR-2026-0001', CURRENT_DATE - 30, 'stored', true),
  ('44444444-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222',
   '33333333-3333-3333-3333-333333333333', 'BR-2026-0002', CURRENT_DATE - 20, 'stored', true)
ON CONFLICT DO NOTHING;

-- `code` is the last MQTT topic segment: it links a message to its sensor.
INSERT INTO sensors (sensor_id, warehouse_id, code, is_active)
VALUES
  ('55555555-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222', 'SENSOR-BR-01', true),
  ('55555555-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222', 'SENSOR-BR-02', true)
ON CONFLICT DO NOTHING;

-- Open and older than the generated readings, which is what allows storing them.
INSERT INTO sensor_assignments (sensor_assignment_id, sensor_id, batch_id, assigned_at, released_at)
VALUES
  ('66666666-0000-0000-0000-000000000001', '55555555-0000-0000-0000-000000000001',
   '44444444-0000-0000-0000-000000000001', NOW() - INTERVAL '30 days', NULL),
  ('66666666-0000-0000-0000-000000000002', '55555555-0000-0000-0000-000000000002',
   '44444444-0000-0000-0000-000000000002', NOW() - INTERVAL '20 days', NULL)
ON CONFLICT DO NOTHING;

COMMIT;
