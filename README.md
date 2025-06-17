# 📁 MFTTransfer

MFTTransfer is a distributed file transfer system designed to move large files between MFT nodes efficiently and reliably. It supports both **one-to-one** and **one-to-many** transfers, ensuring **resumability**, **parallel downloads**, and **scalability** across multiple nodes.

This project was built in response to a system design challenge focused on performance, fault tolerance, and high availability during multi-node file transmission.

---

## 🧩 Architecture Overview

- **MFTTransfer.Api** – Exposes endpoints for initiating uploads and managing transfer sessions.
- **MFTTransfer.BackgroundJobs** – Hosts Kafka consumers and background handlers for chunk processing, retries, and status tracking.
- **MFTTransfer.Domain** – Contains data models, enums, and interfaces for clean architecture boundaries.
- **MFTTransfer.Infrastructure** – Houses caching logic (Redis, memory), blob storage access, Kafka services, and reusable helpers.
- **MFTTransfer.Monitoring** – Implements Prometheus-based metrics and logging.
- **MFTTransfer.Utilities** – Utility functions for chunking, compression, hashing, and Redis helpers.

---

## 🚀 Features

- 🔁 **Reliable transfers** with resumable support (tracked by Redis)
- ⚡ **Parallel chunk uploading/downloading** for speed
- 📡 **Kafka-based communication** between nodes
- 💾 **Blob storage** via Azurite (or Azure Blob in production)
- 🔍 **Prometheus metrics** integration
- 🔄 **Retry handlers** for failed chunks
- 🔒 Designed for **high availability** and **horizontal scalability**

---

## 📂 Folder Highlights

| Project                          | Key Responsibilities                                      |
|----------------------------------|-------------------------------------------------------------|
| `MFTTransfer.Api`               | REST API to initiate and control file transfers             |
| `MFTTransfer.BackgroundJobs`    | Kafka consumers, chunk handlers, retry logic                |
| `MFTTransfer.Domain`            | Data models: `ChunkMessage`, `FileMetadata`, etc.           |
| `MFTTransfer.Infrastructure`    | Services: Kafka, Blob, Redis, caching, constants, interfaces|
| `MFTTransfer.Monitoring`        | `PrometheusMetrics`, `LogCollector`                         |
| `MFTTransfer.Utilities`         | Helpers: chunking, hashing, compression                     |

---

## 🔄 File Transfer Flow

### 🔹 Direct Transfer (MFT_A → MFT_B)

1. MFT_A splits the file into chunks.
2. Each chunk is uploaded to Azurite blob storage.
3. A message is sent to Kafka with transfer metadata.
4. MFT_B listens and downloads each chunk in parallel.
5. Redis tracks the status of downloaded chunks for resumability.

### 🔹 One-to-Many Transfer (MFT_A → MFT_B, MFT_C)

1. MFT_A uploads once and publishes to Kafka.
2. All subscribed nodes (e.g., MFT_B, MFT_C) begin downloading concurrently.
3. Each node independently tracks its download status via Redis.

---

## ⚙️ Tech Stack

- **.NET Core 6++**
- **Kafka** (message queue for inter-node events)
- **Redis** (state tracking for chunk progress)
- **Azurite / Azure Blob Storage**
- **Prometheus** (metrics collection)
- **Docker** (recommended for local setup)

---

## 🧪 How to Run (Local Dev)

```bash
# 1. Clone the repository
git clone https://github.com/tuanlevunhat/MFTTransfer.git
cd MFTTransfer

# 2. Ensure you have:
# - Docker running (for Kafka, Redis, Azurite)
# - .NET 6 SDK installed

# 3. Build & run the services
dotnet build
dotnet run --project MFTTransfer.Api

# 4. Setup other services (background jobs, etc.)
dotnet run --project MFTTransfer.BackgroundJobs
