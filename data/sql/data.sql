CREATE TABLE services (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    team TEXT NOT NULL,
    sla_ms INTEGER NOT NULL,
    language TEXT NOT NULL
);

CREATE TABLE incidents (
    id INTEGER PRIMARY KEY,
    service_id INTEGER REFERENCES services(id),
    severity INTEGER CHECK(severity BETWEEN 1 AND 5),
    description TEXT,
    occurred_at DATETIME,
    resolved_at DATETIME
);

-- Seed with realistic fake data
INSERT INTO services VALUES
(1, 'order-processor', 'payments', 200, 'C#'),
(2, 'inventory-api', 'warehouse', 150, 'Go'),
(3, 'notification-service', 'platform', 500, 'C#')