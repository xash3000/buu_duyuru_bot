# BUÜ Duyuru Bot

Bursa Uludag University's website lacks an announcement notification system, requiring manual page refreshes to check for updates. This project automates that process.

## Features

- **Extensive Coverage**: Provides subscription options for 136 different sources, covering all faculties, departments, and administrative units.
- **Instant Notifications**: Sends targeted alerts via Telegram the moment a new announcement goes live.

## Technical Overview

The core application is built as a robust C# background service.

- **Data Acquisition**: Periodically crawls and scrapes various university department pages to detect new content.
- **Data Sanitization**: Parses and transforms fragmented, inconsistent HTML from legacy university portals into clean, structured data models.
- **Distribution**: Integrates directly with the Telegram Bot API to push formatted content updates to actively subscribed users.
