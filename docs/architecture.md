# Архитектура FleetCare

## Назначение компонентов

- FleetCare.Api — принимает HTTP-запросы и сохраняет телеметрию.
- FleetCare.Worker — обрабатывает события и применяет правила ТО.
- FleetCare.Domain — бизнес-сущности и правила.
- FleetCare.Infrastructure — работа с PostgreSQL и Kafka.
- analytics — анализ температурных аномалий на Python.
- simulator — генерация показаний машин.

## Текущая схема взаимодействия

```mermaid
flowchart TD
    S[Simulator] --> A[ASP.NET Core API]
    A --> DB[(PostgreSQL)]
    A --> O[PostgreSQL Outbox]
    O --> K[Apache Kafka]
    K --> W[.NET Worker]
    K --> P[Python Analytics]
    P --> K
    W --> DB
```

Python читает сохранённую историю температур через роль PostgreSQL только для
чтения и публикует `risk.detected.v1`. .NET Worker читает этот топик и сохраняет
предупреждения для страницы `/dashboard/risks`. [Подробности](analytics.md).

## Главные принципы

1. Domain не зависит от инфраструктурных технологий.
2. События обрабатываются с защитой от дублирования.
3. Принятая телеметрия не теряется при временных сбоях Kafka.
4. Аналитика Python отделена от бизнес-правил .NET.
5. Вся демонстрационная телеметрия создаётся симулятором.
