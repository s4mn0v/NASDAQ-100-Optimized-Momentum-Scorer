<img width="1835" height="1001" alt="image" src="https://github.com/user-attachments/assets/5e84eef4-0f91-4c4e-afc1-6d28ad2f6e59" />

# NASDAQ 100 Optimized Momentum Scorer

This Pine Script v6 indicator is a professional-grade quantitative tool optimized specifically for the NASDAQ 100 (NQ/MNQ). It employs a sophisticated scoring engine that aggregates trend, momentum, and price action data to identify high-probability entries while utilizing advanced filters to prevent trading during unfavorable conditions.

## Highlights

- Optimized for NASDAQ 100 high-volatility environments.
- Multi-factor scoring system including HMA, RSX, MACD Impulse, and EMA 200.
- Dynamic spread filter using real-time ask/bid data to avoid high slippage.
- Intraday price action context via Daily High, Low, and Midpoint tracking.
- Volatility threshold filter to ensure price movement is sufficient for execution.
- Anti-overtrading logic via a state-machine that limits entries to one per trend cycle.

## Overview

The "Optimized" version of this indicator focuses on execution quality. By requiring a confluence of technical modules and passing strict environmental checks (spread and volatility), the script aims to capture momentum bursts during the early stages of a trend.

### Technical Scoring Logic

The signal is generated based on a cumulative score. A score of +4 or higher is required for long signals, while -4 or lower is required for short signals. The score is calculated as follows:

- Hull Moving Average (HMA) Direction: +/- 2 points.
- MACD Impulse Direction: +/- 1 point.
- Jurik RSX Momentum (>50): +/- 1 point.
- HMA Slope (Rate of change): +/- 1 point.
- EMA 200 Alignment: +/- 1 point.
- Daily Range Context (Above/Below Daily Midpoint): +/- 1 point.

## Key Modules

### Trend and Momentum
- Hull Moving Average (HMA): Fast and slow HMA cross detection for core trend definition.
- Jurik RSX: An advanced version of the RSI that eliminates noise while maintaining minimal lag.
- MACD Impulse: Uses a Zero Lag EMA (ZLEMA) to identify rapid shifts in market velocity.

### Execution Filters
- Spread Filter: Monitors the current tick difference between ask and bid prices. Signals are blocked if the spread exceeds the user-defined threshold.
- Volatility Filter: Uses Bollinger Band Width and ATR-based logic to confirm that the market is not in a low-liquidity lateral state.
- Trend Age: A counter that tracks the number of bars since a trend change. This prevents "chasing" a move that has already extended significantly.

### Price Action Context
- Daily Levels: Automatically plots and tracks the current day's High and Low.
- Daily Midpoint: Signals are weighted based on whether price is trading in the upper or lower half of the total daily range.

## Usage

### Visual Signals
- Strong Buy (+): A lime label appearing below the bar indicates a high-scoring long setup with volatility and spread clearance.
- Strong Sell (-): A red label appearing above the bar indicates a high-scoring short setup with volatility and spread clearance.

### Dashboard Monitoring
The real-time dashboard in the top-right corner provides:
- Current Score: Real-time calculation of the scoring engine.
- Market State: Displays "ACTIVE" or "LATERAL" based on Bollinger Band Width.
- Trend Age: Current bar count of the active HMA trend.
- Spread: Real-time tick spread monitoring.

## Installation

1. Open the Pine Editor in TradingView.
2. Create a new "Indicator" script.
3. Replace the default template with the provided code.
4. Save the script and click "Add to Chart."
5. Recommended for use on the NASDAQ 100 (NQ100 / NQ / MNQ) on lower timeframes (1m, 5m, 15m).

## Technical Requirements
- TradingView Pine Script v6.
- Real-time data feed for Ask/Bid values is recommended for the spread filter to function accurately.
