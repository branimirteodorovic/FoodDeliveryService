# 🍕 Food Delivery Platform — Detailed Project Plan

> A portfolio project built by a .NET developer to demonstrate modern backend engineering skills including microservices, containerization, security, caching, telemetry, and AI features. The system will serve four types of users: **Customers**, **Restaurant Managers**, **Delivery Drivers**, and **Support Agents**, overseen by platform **Administrators** who onboard restaurants and provision staff/partner accounts.

---

## Table of Contents

- [Phase 1 — The Foundation](#phase-1--the-foundation)
- [Phase 2 — Real-Time, Performance & Observability](#phase-2--real-time-performance--observability)
- [Phase 3 — AI Features, Load Testing & Production Polish](#phase-3--ai-features-load-testing--production-polish)
- [Technology Reference](#technology-reference)

---

## Phase 1 — The Foundation

> **Goal:** Build the core of the system — user accounts, restaurant menus, and the ability to place and manage orders. By the end of this phase, a customer can browse restaurants, place an order, and a restaurant can accept or reject it.

---

### Feature 1.1 — Solution Structure & Repository Setup

**What it does:**
Before writing any business logic, we establish the skeleton of the entire project. This includes setting up the code repository on GitHub, defining how the code is organized into separate services (each service is like an independent mini-application), and making sure all developers (or in this case, just you) follow consistent conventions from day one.

This feature is purely technical infrastructure — it has no visible functionality yet, but every subsequent feature depends on it being done correctly.

**Tasks:**
- Create a GitHub repository with a monorepo structure (all services live in one repo, in separate folders)
- Define a consistent folder and naming convention for all services
- Create a shared `docker-compose.yml` file so all services can be started locally with a single command
- Add a root-level `README.md` explaining the project

**Technologies:**
- **GitHub** — A platform for storing and versioning code. It also enables collaboration, code reviews, and hosts the CI/CD pipelines that will automatically build and deploy the application.
- **Docker Compose** — A tool that lets you define and run multiple application containers together using a single configuration file. During local development, this means you can start PostgreSQL, RabbitMQ, Redis, and all your services with one command instead of manually launching each one.
- **.NET Solution (.sln)** — The standard way to organise multiple .NET projects under one roof. Each microservice will be its own project inside this solution.

---

### Feature 1.2 — Identity Service (User Accounts & Authentication)

**What it does:**
This is the "front door" of the entire platform. Every person who interacts with the system — whether a customer ordering food, a restaurant manager updating their menu, a delivery person marking an order as delivered, or a support agent resolving a complaint — must first prove who they are.

This service handles:
- **Registration** — Only **Customers** create their own accounts (email, password). Staff and partner accounts (Restaurant Managers, Delivery Drivers, Support Agents) are **not** self-registered — restaurants sign a contract with the platform, and drivers/support are employees. An **Administrator** provisions these accounts and the person activates their own by following an emailed invitation link (see below)
- **Login** — Verifying credentials and issuing a secure digital token (like a temporary ID badge)
- **Role management** — Knowing whether the logged-in person is a Customer, Restaurant Manager, Delivery Driver, Support Agent, or Administrator. A single account may hold more than one role (e.g. a Restaurant Manager who also orders food as a Customer)
- **Account provisioning & invitations** — An Administrator creates staff/partner accounts; the invitee receives an email with a one-time activation link (a token, not a password) and sets their own password to activate. No temporary password is ever emailed
- **Token refresh** — Automatically renewing the session without asking the user to log in again
- **Logout** — Revoking the digital token so it can no longer be used

**Tasks:**
- Set up Duende IdentityServer as a standalone service
- Define the roles: `Customer`, `RestaurantManager`, `DeliveryDriver`, `SupportAgent`, `Administrator`
- Implement customer self-registration, login, and administrator-driven account provisioning (create staff/partner account → email invitation → activate via token)
- Seed an initial Administrator from configuration on startup (defaults in `appsettings.Development.json`; empty in `appsettings.json` so real environments must supply their own)
- Configure JWT (JSON Web Token) issuance with role claims
- Store user accounts in a dedicated PostgreSQL database
- Write integration tests for all auth flows (customer registration, invitation/activation, login, authorization)

**Technologies:**
- **Duende IdentityServer (Community Edition)** — An open-source framework for .NET that implements the industry-standard OAuth 2.0 and OpenID Connect protocols. Instead of writing your own login system from scratch (which is risky from a security perspective), IdentityServer gives you a battle-tested, standards-compliant solution. The Community Edition is free for small projects and startups.
- **OAuth 2.0 / OpenID Connect** — These are open standards for authorization and authentication. OAuth 2.0 answers "what is this application allowed to do?", while OpenID Connect answers "who is this person?". Using standards means any client — web app, mobile app, or third-party service — can integrate with your identity system the same way.
- **JWT (JSON Web Tokens)** — A compact, self-contained token that encodes the user's identity and roles. Once issued, other services can verify this token without calling the Identity Service every time, which makes the system faster and more scalable.
- **PostgreSQL** — An open-source, enterprise-grade relational database. Each microservice gets its own PostgreSQL database to ensure services are truly independent (a principle called "database per service"). User account data is structured and relational, making PostgreSQL the right fit.
- **BCrypt / ASP.NET Identity** — Password hashing libraries that ensure passwords are never stored in plain text. Even if the database is compromised, passwords remain unreadable.

---

### Feature 1.3 — API Gateway

**What it does:**
Imagine the API Gateway as the reception desk of a large office building. Instead of every visitor wandering around looking for the right department, the reception desk directs them to the correct place. Similarly, all requests from mobile apps and web apps go to a single entry point — the API Gateway — which then routes them to the correct internal service.

The Gateway also:
- Verifies that the user's token is valid before letting the request through
- Enforces **rate limiting** — preventing any single user or IP address from making too many requests (protecting against abuse and automated attacks)
- Hides the internal structure of the system from the outside world

**Tasks:**
- Set up YARP (Yet Another Reverse Proxy) as the API Gateway
- Configure routing rules for each downstream service
- Implement JWT token validation at the gateway level
- Implement rate limiting middleware (e.g., 100 requests per minute per user)
- Add request logging (log every incoming request with its route and status code)

**Technologies:**
- **YARP (Yet Another Reverse Proxy)** — A Microsoft-built reverse proxy library for .NET. A reverse proxy sits in front of your services and forwards incoming requests to the right destination. YARP is highly configurable, performant, and integrates naturally with the .NET ecosystem — making it a better fit than heavyweight alternatives like Kong or Nginx for a .NET-first project.
- **Rate Limiting (ASP.NET Core middleware)** — Built into .NET, this feature restricts how many requests a client can make in a given time window. This protects your services from being overwhelmed by a single bad actor or a buggy client sending thousands of requests per second.
- **Reverse Proxy pattern** — An architectural pattern where a single public-facing component forwards requests to internal services. This decouples the outside world from your internal architecture, making it easy to add, move, or replace services without changing client applications.

---

### Feature 1.4 — Restaurant Service

**What it does:**
This service manages everything about restaurants on the platform. An **Administrator** onboards a restaurant — creating the restaurant record and its Restaurant Manager login in one step. From then on the Restaurant Manager sets up their profile, builds their menu, sets prices, marks items as available or sold out, and configures their opening hours.

Customers interact with this service when they browse the list of restaurants or look at a restaurant's menu before ordering.

Specific capabilities:
- An Administrator onboards a restaurant (name, tax identification, address, cuisine type, logo) and provisions its Restaurant Manager account; the manager activates it via an emailed invitation
- The platform's commission (the percentage it takes per order) is set by the Administrator per restaurant at onboarding (negotiated in the partnership contract) and recorded on the restaurant
- A Restaurant Manager can create menu categories (e.g., "Starters", "Main Course", "Desserts")
- They can add menu items with a name, description, price, photo, and availability status
- Customers can search and filter restaurants by cuisine, rating, or proximity
- Customers can view a restaurant's full menu

**Tasks:**
- Build the Administrator onboarding endpoint (creates the restaurant + provisions the Restaurant Manager account via the Identity/Users service)
- Build Restaurant profile update and read endpoints
- Build Menu Category and Menu Item CRUD endpoints
- Implement search and filtering with pagination
- Secure write operations so only the owning Restaurant Manager (or an Administrator) can modify a restaurant's data
- Store all data in a dedicated PostgreSQL database
- Cache frequently accessed menus in Redis (covered fully in Phase 2, but stub it here)

**Technologies:**
- **ASP.NET Core Web API** — The .NET framework for building HTTP-based APIs. Each microservice is an ASP.NET Core application exposing a set of REST endpoints. This is the core technology for all backend services in this project.
- **Entity Framework Core** — An Object-Relational Mapper (ORM) for .NET. Instead of writing raw SQL queries, you define your data as C# classes, and EF Core translates your code into SQL automatically. It also manages database schema changes through "migrations" — versioned scripts that evolve the database structure as your code changes.
- **PostgreSQL** — Used here as the database for restaurant and menu data. Menus are structured, relational data (a restaurant has many categories, each category has many items), which makes a relational database the correct choice.
- **FluentValidation** — A .NET library for defining validation rules for incoming data. For example, ensuring a menu item's price is always a positive number, or that a restaurant name is not empty. This keeps validation logic clean and separate from business logic.

---

### Feature 1.5 — Order Service

**What it does:**
The Order Service is the heart of the platform. It manages the entire lifecycle of an order, from the moment a customer taps "Place Order" to the moment the food is delivered.

An order goes through these stages:
1. **Pending** — The customer has placed the order; waiting for the restaurant to respond
2. **Accepted** — The restaurant has confirmed they will prepare it
3. **Rejected** — The restaurant cannot fulfil the order (e.g., item sold out)
4. **Preparing** — The kitchen is actively preparing the food
5. **Ready for Pickup** — The food is ready; a driver needs to collect it
6. **Out for Delivery** — A driver has picked up the order and is heading to the customer
7. **Delivered** — The customer has received the food
8. **Cancelled** — The order was cancelled (by customer or restaurant, within allowed rules)

The service also calculates the order total, applies any available promotions, and records the chosen payment method (cash on delivery, in this project).

**Tasks:**
- Build order placement endpoint (validates items against current menu prices)
- Implement the order status state machine (strict rules about which transitions are allowed)
- Build endpoints for restaurants to accept/reject/update order status
- Implement idempotency on order placement (placing the same order twice only creates one order)
- Publish domain events to RabbitMQ when order status changes (e.g., `OrderAccepted`, `OrderReady`)
- Store orders in a dedicated PostgreSQL database

**Technologies:**
- **State Machine pattern** — A design pattern where an entity (the order) can only exist in one of a defined set of states, and can only move between states via defined transitions. For example, an order cannot jump from "Pending" directly to "Delivered". Implementing this prevents data corruption and reflects real business rules accurately.
- **Idempotency** — The property that performing the same operation multiple times produces the same result as performing it once. On the web, a user might accidentally tap "Place Order" twice due to a slow network. An idempotency key (a unique ID sent by the client) ensures the second request is recognised as a duplicate and ignored.
- **RabbitMQ** — A message broker — a middleman that receives messages from one service and delivers them to other services that are interested. When an order is accepted, the Order Service publishes an `OrderAccepted` event to RabbitMQ. The Notification Service (which sends push notifications) and the Delivery Service (which finds a driver) both receive this event and react independently. This means services don't need to know about each other, making the system loosely coupled and resilient.
- **Domain Events** — A pattern from Domain-Driven Design (DDD) where significant business occurrences (like "an order was placed") are represented as explicit events. These events can then be stored, replayed, or published to other services, making the system's behaviour auditable and extensible.
- **Outbox Pattern** — A reliability pattern that solves the problem of "what happens if my service saves the order to the database but then crashes before it can publish the event to RabbitMQ?". The solution: save the event to a dedicated "outbox" table in the same database transaction as the order itself. A background process then reads from the outbox and publishes to RabbitMQ, guaranteeing at-least-once delivery.

---

### Feature 1.6 — Notification Service

**What it does:**
This service listens for events from other services (via RabbitMQ) and sends notifications to the right people at the right time.

Examples:
- When a restaurant accepts an order → send a push notification to the customer: *"Your order has been accepted!"*
- When a driver is assigned → notify the customer with the driver's name
- When the food is delivered → notify the customer and prompt them to leave a review
- When a new order arrives → notify the restaurant owner

In this phase, notifications will be implemented as **emails** using a simple email provider. Push notifications (for mobile apps) will be added in Phase 2.

**Tasks:**
- Create a background service that subscribes to RabbitMQ queues
- Map each event type to a notification template
- Implement email sending via SendGrid or SMTP
- Store a notification log in PostgreSQL (for auditing and debugging)
- Handle failed notifications with retry logic

**Technologies:**
- **RabbitMQ (Consumer side)** — The Notification Service subscribes to specific queues in RabbitMQ and processes messages as they arrive. If the service is temporarily down, RabbitMQ holds the messages until the service recovers, ensuring no notifications are lost.
- **MassTransit** — A .NET library that sits on top of message brokers like RabbitMQ and provides a higher-level, developer-friendly API. Instead of dealing with low-level RabbitMQ connections and serialization, MassTransit lets you publish and consume strongly-typed C# messages. It also handles retries, error queues, and sagas (long-running workflows) out of the box.
- **SendGrid / SMTP** — SendGrid is a cloud email delivery service that handles the complexity of sending emails reliably at scale (spam filtering, delivery tracking, bounce handling). For a portfolio project, you can start with SMTP and upgrade to SendGrid for a more production-realistic setup.
- **Background Services (IHostedService)** — A .NET mechanism for running long-lived background tasks within an ASP.NET Core application. The message consumer runs as a background service, continuously listening for new messages without blocking the main API.

---

### Feature 1.7 — CI/CD Pipeline — Phase 1

**What it does:**
Continuous Integration and Continuous Deployment (CI/CD) is the practice of automatically building, testing, and deploying your application every time you push new code. This eliminates manual deployment steps, catches bugs early, and ensures the live environment always reflects the latest approved code.

In Phase 1, the pipeline will:
- Automatically build all services when code is pushed to GitHub
- Run all unit and integration tests
- Build Docker images for each service
- Push those images to Azure Container Registry (a private image storage service)
- Deploy updated services to Azure (using Azure Container Apps for Phase 1, upgrading to AKS in Phase 2)

**Tasks:**
- Write GitHub Actions workflow files for each service
- Set up Azure Container Registry (ACR) to store Docker images
- Configure Azure Container Apps for initial cloud deployment
- Set up separate environments: `development`, `staging`, `production`
- Store secrets (connection strings, API keys) in GitHub Secrets and Azure Key Vault

**Technologies:**
- **GitHub Actions** — A CI/CD platform built directly into GitHub. You define workflows in YAML files, and GitHub automatically runs them when triggered (e.g., on every push to `main`). It's free for public repositories and has generous free limits for private ones.
- **Docker** — A containerisation platform that packages your application and all its dependencies into a "container" — a lightweight, portable, isolated unit. The key benefit: a container runs identically on your laptop, your colleague's machine, and in the cloud. "It works on my machine" becomes irrelevant.
- **Azure Container Registry (ACR)** — A private Docker image repository hosted on Azure. After building a Docker image in the CI pipeline, you push it to ACR, and your deployment infrastructure pulls it from there when spinning up new containers.
- **Azure Container Apps** — A managed Azure service for running containerised applications without managing the underlying infrastructure. It handles scaling, load balancing, and health monitoring automatically. It's a simpler starting point than Kubernetes and is ideal for Phase 1.
- **Azure Key Vault** — A secure, cloud-hosted storage service for secrets, API keys, and connection strings. Instead of putting sensitive values in code or config files (which would expose them in your public GitHub repo), Key Vault stores them securely and your application retrieves them at runtime.

---

## Phase 2 — Real-Time, Performance & Observability

> **Goal:** Make the system fast, visible, and real-time. By the end of this phase, customers can track their driver's location on a map, the system handles high load efficiently through caching, and engineers can diagnose any problem through detailed telemetry and logs.

---

### Feature 2.1 — Delivery Service & Driver Management

**What it does:**
This service manages the delivery side of every order. Once a restaurant marks an order as "Ready for Pickup", the Delivery Service springs into action:

- It finds available drivers near the restaurant
- It assigns the best driver to the order
- The driver receives the assignment on their mobile app and can accept or reject it
- Once accepted, the driver's real-time location is tracked throughout the delivery
- The driver marks the order as "Picked Up" and then "Delivered"

The Delivery Service also maintains a driver's profile — their current status (available, busy, offline), their vehicle type, and their delivery history.

**Tasks:**
- Build driver profile management endpoints
- Implement driver availability status management
- Build order assignment logic (find nearest available driver)
- Create endpoints for drivers to accept/reject assignments
- Implement driver location update endpoint (called by the mobile app every few seconds)
- Store location history in Cosmos DB
- Publish `DriverAssigned`, `OrderPickedUp`, `OrderDelivered` events to RabbitMQ

**Technologies:**
- **Azure Cosmos DB** — A globally distributed NoSQL database from Microsoft. Unlike PostgreSQL (which stores data in structured rows and tables), Cosmos DB stores data as flexible JSON documents, making it ideal for data that doesn't have a fixed structure. Driver location data is a perfect fit: it's high-volume, time-series in nature, and doesn't need complex relational queries. Cosmos DB can handle millions of location writes per second.
- **Geospatial queries** — Cosmos DB supports spatial queries natively, meaning you can ask "find all drivers within 5 kilometres of this coordinate" directly at the database level, without fetching all drivers and calculating distances in your application code.
- **Driver Assignment Algorithm** — A simple but important piece of business logic: given a restaurant's location and a list of available drivers, select the optimal driver. Initially this can be "nearest driver"; later it can factor in driver ratings, current traffic, or order size.

---

### Feature 2.2 — Real-Time Order & Driver Tracking (SignalR)

**What it does:**
One of the most valuable features from a user experience perspective. Instead of the customer having to refresh the page to see their order status, the app updates automatically in real time.

Specifically:
- The customer's screen shows a live map with the driver's moving location
- Order status changes (e.g., "Your food is being prepared" → "Driver is on the way") appear instantly without page refresh
- The restaurant dashboard updates in real time when a new order arrives
- The support dashboard shows live order activity

**Tasks:**
- Set up a SignalR Hub in the Order Service (or a dedicated real-time service)
- Push order status change events to the appropriate customer's browser/app connection
- Push driver location updates to the tracking customer
- Implement connection management (handle reconnects gracefully)
- Secure SignalR connections with the same JWT tokens used by the REST API

**Technologies:**
- **SignalR** — A Microsoft library that enables real-time, bidirectional communication between server and client. Traditional HTTP is "request-response": the client asks, the server answers, connection closes. SignalR keeps a persistent connection open so the server can push data to the client at any time — without the client asking. Under the hood, SignalR prefers WebSockets (a modern web protocol for persistent connections) but automatically falls back to other techniques for older environments.
- **WebSockets** — A communication protocol that provides full-duplex (two-way simultaneous) communication over a single TCP connection. This is what makes real-time tracking possible without the inefficiency of the client repeatedly polling the server ("Are we there yet? Are we there yet?").
- **Azure SignalR Service** — A managed Azure service that hosts the SignalR infrastructure, handling connection management and scaling for you. Without this, each server instance would only know about its own connected clients, making scaling difficult. Azure SignalR Service acts as a central hub that all server instances send messages through.

---

### Feature 2.3 — Caching with Redis

**What it does:**
Caching is the practice of storing the results of expensive operations (like database queries) in a fast, temporary storage layer so they don't need to be recalculated on every request.

Consider a popular restaurant's menu: it might be viewed thousands of times per hour, but the menu rarely changes. Without caching, every single view triggers a database query. With Redis caching, the first request fetches from the database and stores the result in Redis; the next 9,999 requests are served from Redis in under a millisecond.

In this project, Redis will be used for:
- **Menu caching** — Cache restaurant menus for a short period (e.g., 5 minutes)
- **Session caching** — Store user session data for fast access
- **Rate limiting counters** — Track how many requests a user has made (already used in Phase 1's API Gateway)
- **Distributed locking** — Ensure that two simultaneous requests don't both try to assign the same driver to different orders

**Tasks:**
- Set up Azure Cache for Redis
- Implement a caching layer in the Restaurant Service for menu reads
- Add cache invalidation when a restaurant updates their menu
- Implement distributed locking in the Delivery Service for driver assignment
- Add cache-aside pattern helpers as a shared library

**Technologies:**
- **Redis** — An in-memory data store (the data lives in RAM, not on disk) that is extraordinarily fast — capable of processing millions of operations per second. It supports rich data structures (strings, hashes, lists, sets) and has built-in expiration for cache entries. It's the industry standard for caching in high-traffic web applications.
- **Azure Cache for Redis** — The managed Azure version of Redis. Azure handles patching, scaling, replication, and backups, so you focus on using Redis rather than operating it.
- **Cache-Aside Pattern** — A caching strategy where the application is responsible for loading data into the cache. The logic is: (1) check if the data is in the cache; (2) if yes, return it; (3) if no, fetch from the database, store in the cache, then return it. This is the most common and flexible caching pattern.
- **Cache Invalidation** — The process of removing or updating a cached item when the underlying data changes. When a restaurant updates a menu item's price, the cached menu must be invalidated so the next request fetches the fresh data. This is one of the famously hard problems in computer science — getting it wrong leads to users seeing stale data.
- **Distributed Locking** — In a system with multiple service instances running in parallel, a distributed lock ensures that only one instance can perform a critical operation (like assigning a driver) at a time. Redis provides a mechanism called Redlock for this purpose.

---

### Feature 2.4 — Distributed Telemetry & Observability

**What it does:**
Observability is the ability to understand what is happening inside your system by looking at its outputs. In a microservices architecture with 7+ services, when something goes wrong, you need to be able to trace a single customer's request as it travels through the API Gateway → Order Service → Notification Service → Delivery Service, and pinpoint exactly where the failure occurred.

This feature implements three pillars of observability:
- **Traces** — A record of a request's journey through all the services it touched, with timing for each step
- **Metrics** — Numerical measurements over time (e.g., "how many orders per minute?", "what is the average database query time?")
- **Logs** — Structured text records of events, errors, and important moments in the application

**Tasks:**
- Instrument all services with OpenTelemetry SDK
- Export traces, metrics, and logs to Azure Monitor / Application Insights
- Add correlation IDs to all log entries (so you can search for all logs related to one order)
- Create an Application Insights dashboard showing key business and technical metrics
- Set up alerts for error rate spikes and latency degradation
- Add health check endpoints to every service

**Technologies:**
- **OpenTelemetry** — A vendor-neutral, open-source standard for collecting telemetry data (traces, metrics, logs) from applications. By using OpenTelemetry, you can switch your observability backend (e.g., from Azure Monitor to Grafana) without changing your application code. It's the emerging industry standard, backed by Microsoft, Google, and most major cloud providers.
- **Azure Monitor & Application Insights** — Microsoft's cloud observability platform. Application Insights is the application-level component that collects traces and performance data; Azure Monitor is the broader platform for dashboards, alerts, and log analytics. Together, they give you a complete picture of your system's health.
- **Serilog** — A structured logging library for .NET. Unlike traditional logging (which produces plain text lines), Serilog produces structured logs — each entry is a JSON object with named fields. This makes logs searchable and filterable (e.g., "find all logs where OrderId = '12345' and Level = 'Error'").
- **Distributed Tracing** — A technique for tracking a request as it propagates through multiple services. Each service adds its own "span" (a timed operation) to a shared "trace" (the end-to-end journey). When you see a trace in Application Insights, you can see the entire timeline: API Gateway (2ms) → Order Service (45ms, including a 30ms DB query) → RabbitMQ publish (3ms).
- **Health Checks (ASP.NET Core)** — Built-in .NET endpoints that report whether a service is healthy. Kubernetes uses these to decide whether to send traffic to a service instance or restart it. Good health checks verify not just that the application is running, but that its dependencies (database, Redis, RabbitMQ) are also reachable.

---

### Feature 2.5 — Kubernetes Deployment on AKS

**What it does:**
In Phase 1, services were deployed to Azure Container Apps — a simpler managed service. In Phase 2, we upgrade to Azure Kubernetes Service (AKS), the industry-standard container orchestration platform. Kubernetes manages the deployment, scaling, and operation of containerised applications across a cluster of servers.

This upgrade enables:
- **Auto-scaling** — Automatically add more instances of a service during lunch rush; scale back down overnight
- **Self-healing** — If a service crashes, Kubernetes automatically restarts it
- **Rolling deployments** — Deploy a new version of a service without any downtime
- **Resource management** — Define how much CPU and memory each service is allowed to use

**Tasks:**
- Provision an AKS cluster on Azure
- Write Kubernetes manifests (YAML files) for each service: Deployment, Service, ConfigMap, Secret
- Configure Horizontal Pod Autoscaler for Order Service and Delivery Service
- Set up an NGINX Ingress Controller to replace the previous routing
- Update GitHub Actions CI/CD pipeline to deploy to AKS (using `kubectl` or Helm)
- Configure Kubernetes resource limits and requests for each service
- Set up Kubernetes liveness and readiness probes using the health check endpoints from Feature 2.4

**Technologies:**
- **Kubernetes (K8s)** — An open-source system for automating the deployment, scaling, and management of containerised applications. It was originally designed by Google, and is now the de facto standard for running microservices in production. Kubernetes treats your infrastructure as a pool of compute resources and intelligently schedules containers across it.
- **Azure Kubernetes Service (AKS)** — Microsoft's managed Kubernetes offering. Azure manages the Kubernetes control plane (the brain of the cluster) for free; you only pay for the worker nodes (the machines that run your containers). AKS integrates tightly with other Azure services like ACR, Key Vault, and Azure Monitor.
- **Helm** — A package manager for Kubernetes. Instead of managing many individual YAML files, Helm lets you bundle them into a "chart" — a parameterised template. You can deploy the same chart to development, staging, and production by simply changing a values file (e.g., different image tags or replica counts per environment).
- **Horizontal Pod Autoscaler (HPA)** — A Kubernetes feature that automatically adjusts the number of running instances of a service based on CPU usage or custom metrics (like messages in a RabbitMQ queue). This is what enables the system to handle 100,000 users — it scales up during peaks and scales down to save cost during quiet periods.
- **NGINX Ingress Controller** — A Kubernetes component that manages external access to services inside the cluster. It replaces the routing that was handled by Azure Container Apps' built-in load balancer, giving you more fine-grained control over routing rules, TLS termination, and traffic management.

---

### Feature 2.6 — Reviews & Ratings

**What it does:**
After a successful delivery, the customer can rate the restaurant (1–5 stars) and leave a written review. They can also rate the delivery experience separately. Restaurants can see their average rating and individual reviews. A running average rating is displayed on the restaurant listing to help future customers make decisions.

**Tasks:**
- Build review submission endpoint (only available after a confirmed delivery)
- Prevent duplicate reviews per order
- Calculate and cache restaurant average ratings
- Expose restaurant ratings in the Restaurant Service search results
- Build moderation flag endpoint (for support agents to hide abusive reviews)

**Technologies:**
- **Idempotency (revisited)** — Ensuring a customer can only submit one review per order, even if they tap the submit button multiple times.
- **Redis (cached aggregates)** — Rather than recalculating the average rating from all reviews on every restaurant listing request, the average is cached in Redis and updated whenever a new review is submitted.

---

## Phase 3 — AI Features, Load Testing & Production Polish

> **Goal:** Add intelligent, AI-driven features that differentiate the platform, prove it can handle massive load, and bring it to production-ready quality. This phase transforms a good portfolio project into an exceptional one.

---

### Feature 3.1 — AI-Powered Customer Support Chatbot (RAG)

**What it does:**
A conversational chatbot embedded in the customer-facing app that can handle the most common support queries automatically, without human intervention:

- *"Where is my order?"* — Fetches real-time order status
- *"My order was wrong, I want a refund"* — Creates a support ticket with context
- *"What are the opening hours of [restaurant]?"* — Queries the restaurant data
- *"Can I cancel my order?"* — Checks order status and initiates cancellation if eligible

The chatbot uses a technique called **Retrieval-Augmented Generation (RAG)**: rather than relying purely on a general AI model's knowledge, it first retrieves relevant, live data from your databases (order status, restaurant info, FAQ documents) and feeds that context to the AI model to generate an accurate, specific answer.

When the chatbot cannot resolve the issue, it seamlessly escalates to a human support agent with full conversation history.

**Tasks:**
- Set up Azure OpenAI Service (GPT-4o) with a system prompt defining the chatbot's behaviour and boundaries
- Build a context-retrieval layer that fetches the customer's current order, history, and relevant FAQs
- Implement the RAG pipeline: retrieve context → construct prompt → call AI → return response
- Build a chat history store so the conversation has memory within a session
- Implement escalation logic: if the AI responds with low confidence or the customer asks for a human, create a support ticket and hand off to the Support Service
- Add guardrails to prevent the chatbot from answering off-topic questions

**Technologies:**
- **Azure OpenAI Service** — Microsoft's managed hosting of OpenAI's models (including GPT-4o) within the Azure environment. Using the Azure-hosted version rather than OpenAI directly is important for enterprise scenarios because it keeps your data within your Azure tenant, subject to Azure's data privacy commitments.
- **RAG (Retrieval-Augmented Generation)** — A technique that improves AI accuracy by giving the model fresh, specific data at inference time. Without RAG, the AI only knows what it was trained on and cannot know your customer's current order status. With RAG, you retrieve that data from your database and include it in the prompt, enabling the AI to give a specific, accurate answer.
- **Semantic Kernel** — A Microsoft open-source SDK for .NET that simplifies building AI applications. It provides abstractions for managing prompts, calling AI models, chaining AI operations together ("plugins"), and integrating external data sources. It's the recommended way to build AI features in a .NET application.
- **Vector Database / Embeddings** — To handle FAQ retrieval efficiently, support documents are converted into numerical representations called "embeddings" (using an AI embedding model) and stored in a vector-capable store (Azure AI Search or Cosmos DB with vector support). When a customer asks a question, it's also converted to an embedding, and the most semantically similar FAQ entries are retrieved — even if the exact words don't match.

---

### Feature 3.2 — Personalised Restaurant & Dish Recommendations

**What it does:**
The home screen for each customer shows personalised restaurant and dish recommendations based on their order history, time of day, and what similar customers enjoy.

Examples:
- A customer who orders sushi every Friday evening sees Japanese restaurants featured on Friday afternoons
- A customer who frequently orders vegetarian food sees vegetarian options highlighted
- "People who ordered from this restaurant also liked…" style recommendations

**Tasks:**
- Build an order history analysis endpoint that summarises a customer's preferences
- Implement a prompt-based recommendation engine: send the customer's history and available restaurants to the AI model and ask it to generate ranked recommendations with reasoning
- Cache recommendations per customer (refreshed when a new order is completed)
- Build a "trending dishes" feature (most ordered items in the last 24 hours, per neighbourhood)
- A/B test two recommendation strategies and record which drives more orders

**Technologies:**
- **Azure OpenAI (GPT-4o)** — Used here as a recommendation reasoning engine. By providing it with a structured summary of the customer's preferences and the available restaurant catalogue, the model can generate nuanced, explainable recommendations. This is simpler to implement than training a custom ML model and produces very good results for a portfolio demonstration.
- **Prompt Engineering** — The practice of carefully crafting the instructions given to an AI model to reliably produce the desired output format and quality. For recommendations, a well-engineered prompt will specify the output format (JSON list of restaurant IDs with reasoning), tone, and constraints (don't recommend restaurants that are currently closed).
- **Redis (recommendation cache)** — Generating AI recommendations involves calling an external API, which is slower and more expensive than a database query. Caching the result per customer means repeat visits are instant, and the AI is only called when new information warrants fresh recommendations.

---

### Feature 3.3 — AI-Powered Estimated Delivery Time (ETA)

**What it does:**
Rather than showing a static "30-45 minutes" estimate, the system calculates a dynamic, data-driven ETA for each order based on multiple real factors:

- The restaurant's historical average preparation time for that order size
- Current restaurant busyness (how many active orders do they have?)
- Distance between the restaurant and the customer
- Time of day and historical delivery speed for this route/area
- Current driver availability in the area

As the delivery progresses, the ETA is continuously refined and updated on the customer's tracking screen.

**Tasks:**
- Collect historical order data: placement time, preparation time, pickup time, delivery time
- Build an ETA calculation service that combines rule-based logic with AI inference
- Expose an ETA endpoint called by the Order Service at placement time
- Recalculate ETA when the driver is assigned (more accurate distance now known)
- Feed ETA data back into the system to improve future predictions
- Display live, updating ETA on the customer tracking screen (via SignalR from Feature 2.2)

**Technologies:**
- **Azure OpenAI / Azure Machine Learning** — Two complementary approaches: for a portfolio project, you can use Azure OpenAI with a prompt that includes historical averages and current context to produce an ETA estimate. For a more rigorous demonstration, Azure ML can train a lightweight regression model on your historical order data. The portfolio value comes from demonstrating that you understand the problem and have implemented a data-driven solution.
- **Feature Engineering** — The process of transforming raw data (order timestamps, coordinates) into meaningful inputs for a model (e.g., "restaurant busyness score", "estimated drive time in minutes"). Good feature engineering is often more impactful than algorithm choice.
- **Feedback Loop** — Recording predicted ETAs alongside actual delivery times allows the system to measure and improve its accuracy over time. Implementing this demonstrates understanding of the full ML lifecycle, not just inference.

---

### Feature 3.4 — Fraud & Anomaly Detection

**What it does:**
Automatically detects suspicious behaviour patterns that could indicate fraud or abuse:

- A customer placing many orders and cancelling before pickup (abusing a promotion)
- An unusually high number of "order not received" complaints from one customer
- A driver marking orders as delivered without actually moving to the delivery address
- A sudden spike in orders from a new account (could be a bot or stolen card being tested)

Detected anomalies are flagged for review by the support team rather than automatically acted upon, avoiding false positives that penalise legitimate customers.

**Tasks:**
- Define a set of rule-based fraud signals (cancellation rate, complaint rate, location mismatch)
- Implement anomaly scoring: each order and account gets a running risk score
- Integrate Azure Anomaly Detector for time-series anomaly detection (e.g., unusual order volume spikes)
- Send high-risk flags as events to the Support Service for manual review
- Build a fraud dashboard in the support web app

**Technologies:**
- **Azure Anomaly Detector** — An Azure AI service that applies machine learning to time-series data to identify anomalies automatically. You don't need to train a model yourself; you send it your time-series data (e.g., order counts per hour) and it returns anomaly scores. This demonstrates practical use of a real Azure AI service with minimal setup.
- **Risk Scoring** — A pattern where each entity (customer, order, driver) accumulates a composite score based on multiple signals. No single signal may be conclusive, but a combination (new account + high-value order + delivery address in a different city from usual) can reliably flag suspicious activity. This is how real fraud detection systems work.
- **Event-Driven Architecture (revisited)** — Fraud signals are published as events to RabbitMQ, which the Support Service consumes to create review tasks. This keeps the fraud detection logic decoupled from the support workflow.

---

### Feature 3.5 — Load Testing & Scalability Demonstration

**What it does:**
This is not a user-facing feature, but it is one of the most important for a portfolio project. Recruiters and interviewers often ask: *"How do you know this system can handle real traffic?"* This feature answers that question with evidence.

We simulate the traffic of 100,000 concurrent users using automated load testing tools, observe how the system behaves under pressure, and document the results — including auto-scaling events, response time percentiles, and error rates.

**Tasks:**
- Write load test scripts using k6 that simulate realistic user behaviour (browse restaurants → place order → track delivery)
- Run baseline test: measure performance with no load
- Run ramp-up test: gradually increase to 100,000 virtual users over 10 minutes
- Run spike test: sudden surge to 50,000 users, then back to baseline
- Document: requests per second, p95/p99 response times, error rates, auto-scaling events
- Identify bottlenecks and resolve them (likely: database connection pool limits, unoptimised queries)
- Include results (graphs, numbers) in the GitHub README as proof

**Technologies:**
- **k6** — An open-source, developer-friendly load testing tool where you write test scripts in JavaScript. k6 can simulate thousands of virtual users making real HTTP requests, and produces detailed performance reports. It integrates with Azure Load Testing for cloud-scale execution.
- **Azure Load Testing** — A managed Azure service that runs k6 (and other load testing frameworks) at massive scale from Azure's infrastructure — simulating traffic from multiple regions simultaneously. Critically, it integrates with Application Insights, so you see load test results alongside your system metrics in one dashboard.
- **Percentile metrics (p95, p99)** — Average response time is misleading because a few very slow requests can be hidden in the average. Percentile metrics tell you: "95% of requests completed in under 200ms" (p95 = 200ms). The p99 tells you about the worst 1% of user experiences. Production SLAs are typically defined in percentiles, so understanding and reporting them demonstrates production engineering maturity.

---

### Feature 3.6 — Support Service & Ticketing

**What it does:**
Customer support agents use a web dashboard to:
- View all active and past support tickets
- See the full order history and chat transcript for each ticket
- Update ticket status (Open, In Progress, Resolved, Escalated)
- Issue refunds (recorded as a refund request — no actual payment processing)
- Communicate with customers via the platform's messaging system
- View the AI chatbot conversation that preceded the human escalation

**Tasks:**
- Build ticket CRUD endpoints
- Implement ticket assignment to support agents
- Build internal messaging between support agent and customer
- Implement refund request workflow (creates a record; no actual payment processing)
- Build a support analytics summary: average resolution time, tickets per day, most common issue types

**Technologies:**
- **Role-Based Access Control (RBAC)** — Enforcing that only users with the `support` role can access the support service endpoints. This is enforced at the API Gateway level (JWT role claim check) and repeated at the service level as a defence-in-depth measure.
- **Audit Logging** — All actions taken by support agents (status changes, refund requests) are logged with the agent's ID, timestamp, and reason. This is critical for accountability and dispute resolution, and it's a feature that demonstrates understanding of real-world operational requirements.

---

### Feature 3.7 — Final Production Hardening

**What it does:**
A final pass over the entire system to bring it to production-ready quality. This covers security hardening, documentation, and the things that are easy to skip when building a portfolio project but are immediately noticed by experienced engineers reviewing the code.

**Tasks:**
- **Security audit:** Ensure all endpoints validate JWT tokens, all user inputs are sanitised, no secrets are committed to Git, database connections use least-privilege accounts
- **API documentation:** Ensure Swagger/Scalar docs are complete and accurate for all services
- **README:** Write a comprehensive GitHub README with architecture diagram, technology choices, setup instructions, and load test results
- **Architecture diagram:** Create a visual diagram of all services, databases, and message flows
- **Dependency scanning:** Enable GitHub Dependabot to automatically flag vulnerable NuGet packages
- **Final cost review:** Review Azure resource usage and optimise for cost where possible (right-sizing VMs, using spot instances for non-critical workloads)

**Technologies:**
- **Swagger / Scalar** — API documentation tools that automatically generate interactive documentation from your ASP.NET Core controllers. A recruiter or technical reviewer can open the Swagger UI and explore every endpoint, its parameters, and its response schemas — without reading a single line of code.
- **GitHub Dependabot** — A GitHub feature that automatically scans your dependencies for known security vulnerabilities and opens pull requests to update them. Enabling it demonstrates awareness of supply chain security — an increasingly important topic in professional software development.
- **OWASP Top 10** — A well-known list of the ten most critical web application security risks (SQL injection, broken authentication, exposed sensitive data, etc.). A final security review against this checklist demonstrates security awareness beyond just "I used HTTPS".

---

## Technology Reference

A quick summary of every major technology used in this project, why it was chosen, and where to learn more.

| Technology | Category | Why This Project Uses It |
|---|---|---|
| .NET 8 / ASP.NET Core | Backend framework | Industry-leading performance; native microservice support |
| PostgreSQL | Relational database | Open-source, enterprise-grade; one DB per service |
| Azure Cosmos DB | NoSQL database | High-throughput, geospatial queries for location tracking |
| Redis / Azure Cache for Redis | Caching & distributed locking | Sub-millisecond reads; handles menu and session caching |
| RabbitMQ + MassTransit | Message broker | Decouples services; reliable event delivery |
| Duende IdentityServer | Authentication & authorisation | Standards-compliant OAuth2/OIDC; multi-role support |
| Docker | Containerisation | Consistent environments from dev to production |
| Kubernetes / AKS | Container orchestration | Auto-scaling; self-healing; zero-downtime deployments |
| Helm | Kubernetes packaging | Parameterised, reusable deployment templates |
| GitHub Actions | CI/CD | Automated build, test, and deploy on every push |
| Azure Container Registry | Image storage | Private Docker image repository integrated with AKS |
| Azure Key Vault | Secrets management | Secure, audited storage for credentials and API keys |
| YARP | API Gateway / reverse proxy | .NET-native routing, rate limiting, and auth enforcement |
| SignalR + Azure SignalR Service | Real-time communication | Driver tracking; live order status updates |
| OpenTelemetry | Observability instrumentation | Vendor-neutral traces, metrics, and logs |
| Azure Monitor / App Insights | Observability backend | Dashboards, alerts, and distributed trace visualisation |
| Serilog | Structured logging | Searchable, structured log entries with correlation IDs |
| Azure OpenAI Service (GPT-4o) | AI / LLM | Chatbot, recommendations, ETA reasoning |
| Semantic Kernel | AI SDK for .NET | Simplified prompt management and AI pipeline orchestration |
| Azure Anomaly Detector | AI / anomaly detection | Fraud signal detection without training a custom model |
| k6 + Azure Load Testing | Performance testing | Simulate 100,000 users; prove scalability claims |
| Entity Framework Core | ORM | Type-safe database access; schema migrations |
| FluentValidation | Input validation | Clean, testable validation rules for API inputs |
| Swagger / Scalar | API documentation | Interactive, always-current API documentation |

---

*This plan is intended to be executed incrementally. Each feature within a phase builds on the previous ones. Phase 1 alone produces a deployable, functional system. Each subsequent phase adds meaningful depth and demonstrates progressively more advanced engineering skills.*
