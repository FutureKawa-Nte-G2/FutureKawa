-- Jeu de données minimal pour le test de bout en bout de la chaîne MQTT.
--
-- Ce n'est PAS le seed de démonstration (issue #15) : c'est la fixture
-- strictement nécessaire pour qu'un relevé publié soit conservé. Le consumer
-- d'ingestion écarte en effet tout relevé dont le capteur est inconnu, inactif,
-- ou ne suivait aucun lot au moment de la mesure — comportement voulu, pas une
-- erreur. Sans ces six lignes, le test passerait sans rien écrire.
--
-- Idempotent : rejouable sans erreur sur une base déjà peuplée.

BEGIN;

-- Seuils : 20 °C ± 3 et 55 % ± 10, alignés sur les valeurs par défaut du
-- simulateur. Un capteur nominal reste dans la bande, un capteur en dérive
-- en sort.
INSERT INTO countries (country_id, country_name, country_code,
                       nominal_temp, tolerance_temp, nominal_humidity, tolerance_humidity)
VALUES ('11111111-1111-1111-1111-111111111111', 'Brésil', 'BR', 20, 3, 55, 10)
ON CONFLICT DO NOTHING;

INSERT INTO warehouses (warehouse_id, country_id, warehouse_name, warehouse_ref)
VALUES ('22222222-2222-2222-2222-222222222222',
        '11111111-1111-1111-1111-111111111111', 'Entrepôt Santos', 'WH-BR-001')
ON CONFLICT DO NOTHING;

INSERT INTO farms (farm_id, country_id, farm_name, farm_ref)
VALUES ('33333333-3333-3333-3333-333333333333',
        '11111111-1111-1111-1111-111111111111', 'Fazenda Cerrado', 'FARM-BR-001')
ON CONFLICT DO NOTHING;

-- Deux lots : un témoin qui doit rester conforme, un qui doit basculer.
INSERT INTO batches (batch_id, warehouse_id, farm_id, batch_ref, stored_at, batch_status, is_compliant)
VALUES
  ('44444444-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222',
   '33333333-3333-3333-3333-333333333333', 'BR-2026-0001', CURRENT_DATE - 30, 'in_stock', true),
  ('44444444-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222',
   '33333333-3333-3333-3333-333333333333', 'BR-2026-0002', CURRENT_DATE - 20, 'in_stock', true)
ON CONFLICT DO NOTHING;

-- Le `code` est le dernier segment du topic MQTT : c'est lui qui relie le
-- message publié à une ligne de cette table.
INSERT INTO sensors (sensor_id, warehouse_id, code, is_active)
VALUES
  ('55555555-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222', 'SENSOR-BR-01', true),
  ('55555555-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222', 'SENSOR-BR-02', true)
ON CONFLICT DO NOTHING;

-- Assignations ouvertes (released_at NULL) et antérieures aux relevés générés :
-- c'est la condition qui autorise l'écriture.
INSERT INTO sensor_assignments (sensor_assignment_id, sensor_id, batch_id, assigned_at, released_at)
VALUES
  ('66666666-0000-0000-0000-000000000001', '55555555-0000-0000-0000-000000000001',
   '44444444-0000-0000-0000-000000000001', NOW() - INTERVAL '30 days', NULL),
  ('66666666-0000-0000-0000-000000000002', '55555555-0000-0000-0000-000000000002',
   '44444444-0000-0000-0000-000000000002', NOW() - INTERVAL '20 days', NULL)
ON CONFLICT DO NOTHING;

COMMIT;
