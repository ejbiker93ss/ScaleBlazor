# Pallet Scale System

A real-time pallet scale monitoring system built with Blazor WebAssembly and ASP.NET Core.

## Features

- **Live Reading Display**: Real-time weight readings that update every 2 seconds
- **Capture Reading**: Save scale readings to the database
- **Last 10 Readings**: Scrollable list of recent measurements with timestamps
- **Pallet Management**: Track pallets with reading counts and average weights
- **Complete Pallet**: Mark current pallet as complete and start a new one
- **Daily Averages Chart**: Visual bar chart showing weight trends over the last 10 days
- **Today's Timeline Chart**: Line graph displaying all readings captured today
- **SQLite Database**: Persistent storage for all scale readings and pallet data

## Technology Stack

- **Frontend**: Blazor WebAssembly (NET 7)
- **Backend**: ASP.NET Core Web API (.NET 7)
- **Database**: SQLite with Entity Framework Core
- **Charts**: Chart.js
- **Styling**: Custom CSS with gradient background

## Running the Project

1. Restore dependencies:
   ```
   dotnet restore
   ```

2. Run the server:
   ```
   dotnet run --project ScaleBlazor\Server\ScaleBlazor.Server.csproj
   ```

3. Open your browser and navigate to the URL displayed in the terminal (typically https://localhost:5001 or http://localhost:5000)

## Database

The application uses SQLite and will automatically create the database file `scale.db` in the Server project directory on first run. Sample data is seeded automatically.

## API Endpoints

- `GET /api/scale/current` - Get the most recent scale reading
- `GET /api/scale/readings` - Get last N readings (default: 10)
- `GET /api/scale/readings/today` - Get all readings from today
- `GET /api/scale/daily-averages` - Get daily average weights
- `POST /api/scale/capture` - Capture a new reading
- `GET /api/pallets` - Get pallet list
- `GET /api/pallets/active` - Get the current active pallet
- `POST /api/pallets/complete` - Complete current pallet and create new one

## Project Structure

- **ScaleBlazor.Client**: Blazor WebAssembly frontend
- **ScaleBlazor.Server**: ASP.NET Core backend with API controllers
- **ScaleBlazor.Shared**: Shared data models between client and server
