#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript
{
	public enum EntryModeType
	{
		Confirmacion,
		Impulso,
		Ambos
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{
	// ---------------------------------------------------------------------
	// This is a corrected version of the original indicator. The core fix:
	//
	//   - Calculate switched from OnBarClose to OnEachTick.
	//   - All "structure" state (swing pivots, trend, score, filters) is
	//     computed exactly ONCE per bar, using only CLOSED bar data (never
	//     the live/forming bar). This is what removes the repainting/flicker
	//     risk that OnEachTick would otherwise introduce.
	//   - The actual entry TRIGGER (the breakout check) is evaluated on every
	//     tick against the live price, so the arrow appears at the real
	//     instant price crosses the level - not after the bar fully closes.
	//   - A visual (non-order-sending) projection of the Stop/Target levels
	//     is drawn forward from the entry, and marked "SL"/"TP" on whichever
	//     bar actually reaches that level.
	//
	// This indicator only draws information. It does not place, modify, or
	// manage any real orders - pair it with your own execution logic (e.g.
	// an ATM template or a strategy) if you want it to actually trade.
	// ---------------------------------------------------------------------
	public class TheIndicatorOptimized : Indicator
	{
		private HMA hmaL, hmaS;
		private HMA hma50; // HMA(50): plotted for visual reference AND used as a trend-alignment filter (see UseHma50Filter)
		private ATR atr;
		private Bollinger bb;
		private EMA ema200, macd_e1, macd_e2, macd_signal;

		private Series<double> f28, f30, f38, f40, f48, f50;
		private Series<double> f58, f60, f68, f70, f78, f80;

		private double dHigh, dLow;
		private int trendAge = 0;
		private bool entryAllowed = true;
		private bool impulseAllowed = true;
		private bool lastHmaUp = false;

		private double lastSwingHigh = double.MinValue;
		private double lastSwingLow  = double.MaxValue;

		// --- Bar-scoped structure state: written once per bar in
		// UpdateBarStructure(), read every tick in CheckEntryTriggers().
		// Never depends on the live/forming bar, so it can't flicker. ---
		private bool hmaUp;
		private double zlema;
		private int score;
		private bool anchorLong, anchorShort;
		private bool hma50TrendLong, hma50TrendShort;
		private bool structureLongOK, structureShortOK;
		private bool extensionOK;
		private bool marketActive, volatilityOK;
		private double rsxVal = 50;
		private bool pendingNewSwingHigh, pendingNewSwingLow;

		// --- Visual-only projected trade tracking (this indicator does not
		// send orders; these fields only drive the SL/TP projection lines) ---
		private bool longActive, shortActive;
		private double longStop, longTarget;
		private double shortStop, shortTarget;
		private int longEntryBar, shortEntryBar;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Indicador Optimizado con Scoring, Filtros y Estructura Swing para NQ - entradas en tiempo real, sin repintado";
				Name										= "TheIndicatorOptimized";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= true;
				DisplayInDataBox							= true;

				HmaL_len = 21; HmaS_len = 8; Rsx_len = 14; Atr_len = 100;
				BbLen = 20; MinBBW = 0.5; MaxAge = 15; Macd_ma = 34;
				Macd_sig = 9; MaxSpread = 2.5;

				AtrMultiplier = 1.5;
				UseAnchor = true;
				UseHma50Filter = true;

				UseSwingFilter   = true;
				SwingStrength    = 3;
				AllowReentry     = true;
				MaxExtensionATR  = 3.0;
				AtrTPMultiplier  = 3.0;

				AnchorMultiplier = 5;
				WarmupBars       = 201;

				EntryMode              = EntryModeType.Ambos;
				ImpulseScoreThreshold  = 2;
				RequireRsxExtreme      = true;
				RsxOversold            = 30;
				RsxOverbought          = 70;

				ProjectionBars = 20;

				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "BBUpper");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "BBLower");
				AddPlot(new Stroke(Brushes.Goldenrod, DashStyleHelper.Dot, 1), PlotStyle.Line, "DayHigh");
				AddPlot(new Stroke(Brushes.Purple, DashStyleHelper.Dash, 1), PlotStyle.Line, "DayLow");
				AddPlot(new Stroke(Brushes.DarkOrange, DashStyleHelper.Solid, 2), PlotStyle.Line, "Hma50Plot");
			}
			else if (State == State.Configure)
			{
				if (UseAnchor)
					AddDataSeries(BarsPeriod.BarsPeriodType, BarsPeriod.Value * AnchorMultiplier);
			}
			else if (State == State.DataLoaded)
			{
				hmaL = HMA(HmaL_len);
				hmaS = HMA(HmaS_len);
				hma50 = HMA(50);
				atr = ATR(Atr_len);
				bb = Bollinger(2.0, BbLen);
				ema200 = EMA(200);

				macd_e1 = EMA(Macd_ma);
				macd_e2 = EMA(macd_e1, Macd_ma);
				macd_signal = EMA(macd_e1, Macd_sig);

				f28 = new Series<double>(this); f30 = new Series<double>(this);
				f38 = new Series<double>(this); f40 = new Series<double>(this);
				f48 = new Series<double>(this); f50 = new Series<double>(this);
				f58 = new Series<double>(this); f60 = new Series<double>(this);
				f68 = new Series<double>(this); f70 = new Series<double>(this);
				f78 = new Series<double>(this); f80 = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar == 0)
			{
				BBUpper[0] = double.NaN;
				BBLower[0] = double.NaN;
				DayHigh[0] = double.NaN;
				DayLow[0] = double.NaN;
				Hma50Plot[0] = double.NaN;
			}

			if (BarsInProgress != 0) return;

			string warmupStatus = UseAnchor
				? string.Format("Barras: {0}/{1} | Ancla: {2}/{3}", CurrentBar, WarmupBars, CurrentBars[1], WarmupBars)
				: string.Format("Barras: {0}/{1}", CurrentBar, WarmupBars);
			Draw.TextFixed(this, "WarmupDiag", warmupStatus, TextPosition.BottomRight);

			if (CurrentBar < WarmupBars || (UseAnchor && CurrentBars[1] < WarmupBars))
			{
				DayHigh[0] = double.NaN;
				DayLow[0] = double.NaN;
				return;
			}

			if (Bars.IsFirstBarOfSession) { dHigh = High[0]; dLow = Low[0]; }
			else { dHigh = Math.Max(dHigh, High[0]); dLow = Math.Min(dLow, Low[0]); }

			// STRUCTURE: once per bar, from closed data only.
			if (IsFirstTickOfBar && CurrentBar >= 2)
				UpdateBarStructure();

			// TRIGGER + PROJECTION: every tick, against the live price.
			if (CurrentBar >= 2)
			{
				CheckEntryTriggers();
				UpdateProjectedExits();
			}

			BBUpper[0] = bb.Upper[0];
			BBLower[0] = bb.Lower[0];
			DayHigh[0] = dHigh;
			DayLow[0] = dLow;
			Hma50Plot[0] = hma50[0]; // also drives hma50TrendLong/Short via UpdateBarStructure
		}

		// Computed once per bar (at the first tick of the new bar), using
		// only the bar that just closed (index 1) and earlier. Index 0 (the
		// live, still-forming bar) is never read here - that's what keeps
		// this stable for the rest of the bar instead of flickering.
		private void UpdateBarStructure()
		{
			rsxVal = CalculateRSX();
			double dailyMid = (dHigh + dLow) / 2;

			volatilityOK = (High[1] - Low[1]) > (atr[1] * 0.2);
			marketActive = (bb.Upper[1] - bb.Lower[1]) > (bb.Middle[1] * (MinBBW / 100));

			anchorLong  = !UseAnchor || (Close[1] > ema200[1] && Closes[1][1] > EMA(BarsArray[1], 200)[1]);
			anchorShort = !UseAnchor || (Close[1] < ema200[1] && Closes[1][1] < EMA(BarsArray[1], 200)[1]);

			// HMA(50) trend filter: price must be on the HMA50 side that
			// matches the trade direction. Toggle with UseHma50Filter if you
			// want the visual-only behavior back.
			hma50TrendLong  = !UseHma50Filter || Close[1] > hma50[1];
			hma50TrendShort = !UseHma50Filter || Close[1] < hma50[1];

			hmaUp = hmaS[1] > hmaL[1];
			if (hmaUp != lastHmaUp) { trendAge = 0; entryAllowed = true; impulseAllowed = true; }
			else trendAge++;
			lastHmaUp = hmaUp;

			double prevSwingHigh = lastSwingHigh;
			double prevSwingLow  = lastSwingLow;
			pendingNewSwingHigh = false;
			pendingNewSwingLow  = false;

			if (UseSwingFilter && CurrentBar >= SwingStrength * 2 + 1)
			{
				if (IsSwingHigh(SwingStrength))
				{
					lastSwingHigh = High[SwingStrength + 1];
					pendingNewSwingHigh = true;
					Draw.Dot(this, "SwH" + (CurrentBar - SwingStrength - 1), true, SwingStrength + 1, High[SwingStrength + 1] + (TickSize * 4), Brushes.OrangeRed);
				}
				if (IsSwingLow(SwingStrength))
				{
					lastSwingLow = Low[SwingStrength + 1];
					pendingNewSwingLow = true;
					Draw.Dot(this, "SwL" + (CurrentBar - SwingStrength - 1), true, SwingStrength + 1, Low[SwingStrength + 1] - (TickSize * 4), Brushes.DodgerBlue);
				}
			}

			zlema = macd_e1[1] + (macd_e1[1] - macd_e2[1]);
			score = 0;
			score += hmaUp ? 2 : -2;
			score += (zlema > macd_signal[1]) ? 1 : -1;
			score += (rsxVal > 50) ? 1 : -1;
			score += (hmaS[1] > hmaS[2]) ? 1 : -1;
			score += (Close[1] > dailyMid) ? 1 : -1;
			score += (Close[1] > hma50[1]) ? 1 : -1;

			structureLongOK  = !UseSwingFilter || lastSwingLow == double.MaxValue  || Low[1]  >= lastSwingLow;
			structureShortOK = !UseSwingFilter || lastSwingHigh == double.MinValue || High[1] <= lastSwingHigh;

			extensionOK = true;
			if (MaxExtensionATR > 0)
			{
				extensionOK = hmaUp
					? (Close[1] - dLow)  <= atr[1] * MaxExtensionATR
					: (dHigh - Close[1]) <= atr[1] * MaxExtensionATR;
			}

			if (AllowReentry && UseSwingFilter)
			{
				if (hmaUp && pendingNewSwingLow && lastSwingLow > prevSwingLow) { entryAllowed = true; impulseAllowed = true; }
				if (!hmaUp && pendingNewSwingHigh && lastSwingHigh < prevSwingHigh) { entryAllowed = true; impulseAllowed = true; }
			}
		}

		// Evaluated on EVERY tick against the live price (Close[0]/High[0]/
		// Low[0]) - this is what makes the arrow appear at the real moment
		// the breakout happens, instead of after the bar closes.
		private void CheckEntryTriggers()
		{
			double currentSpread = (State == State.Realtime) ? (GetCurrentAsk() - GetCurrentBid()) / TickSize : 0;
			bool spreadOK = State != State.Realtime || currentSpread <= MaxSpread;

			if (entryAllowed && marketActive && volatilityOK && spreadOK && trendAge <= MaxAge)
			{
				if (hmaUp && score >= 4 && anchorLong && hma50TrendLong && structureLongOK && extensionOK && Close[0] > High[1] && !longActive)
				{
					Draw.ArrowUp(this, "L" + CurrentBar, true, 0, Low[0] - (atr[0] * AtrMultiplier), Brushes.Lime);
					Draw.Text(this, "EL" + CurrentBar, "Entry " + Close[0].ToString("F2"), 0, Close[0] + (TickSize * 6), Brushes.Lime);
					StartLongProjection();
					entryAllowed = false;
				}
				else if (!hmaUp && score <= -4 && anchorShort && hma50TrendShort && structureShortOK && extensionOK && Close[0] < Low[1] && !shortActive)
				{
					Draw.ArrowDown(this, "S" + CurrentBar, true, 0, High[0] + (atr[0] * AtrMultiplier), Brushes.Red);
					Draw.Text(this, "ES" + CurrentBar, "Entry " + Close[0].ToString("F2"), 0, Close[0] - (TickSize * 6), Brushes.Red);
					StartShortProjection();
					entryAllowed = false;
				}
			}

			if ((EntryMode == EntryModeType.Impulso || EntryMode == EntryModeType.Ambos)
				&& impulseAllowed && marketActive && volatilityOK && spreadOK && trendAge <= MaxAge && UseSwingFilter)
			{
				bool rsxLongOK  = !RequireRsxExtreme || rsxVal <= (RsxOversold + 10);
				bool rsxShortOK = !RequireRsxExtreme || rsxVal >= (RsxOverbought - 10);

				if (pendingNewSwingLow && hmaUp && anchorLong && hma50TrendLong && score >= ImpulseScoreThreshold && rsxLongOK && !longActive)
				{
					Draw.Diamond(this, "IL" + CurrentBar, true, 0, Low[0] - (atr[0] * AtrMultiplier * 0.5), Brushes.Cyan);
					StartLongProjection();
					impulseAllowed = false;
				}
				else if (pendingNewSwingHigh && !hmaUp && anchorShort && hma50TrendShort && score <= -ImpulseScoreThreshold && rsxShortOK && !shortActive)
				{
					Draw.Diamond(this, "IS" + CurrentBar, true, 0, High[0] + (atr[0] * AtrMultiplier * 0.5), Brushes.Fuchsia);
					StartShortProjection();
					impulseAllowed = false;
				}
			}
		}

		private void StartLongProjection()
		{
			longActive   = true;
			longStop     = Low[0] - (atr[0] * AtrMultiplier);
			longTarget   = AtrTPMultiplier > 0 ? Close[0] + atr[0] * AtrTPMultiplier : 0;
			longEntryBar = CurrentBar;
		}

		private void StartShortProjection()
		{
			shortActive   = true;
			shortStop     = High[0] + (atr[0] * AtrMultiplier);
			shortTarget   = AtrTPMultiplier > 0 ? Close[0] - atr[0] * AtrTPMultiplier : 0;
			shortEntryBar = CurrentBar;
		}

		// Draws (and keeps extending) the projected Stop/Target lines for
		// whichever signal is active, and marks the exact bar where price
		// reaches either level. Visual only - no orders are sent.
		private void UpdateProjectedExits()
		{
			if (longActive)
			{
				int barsAgoStart = CurrentBar - longEntryBar;
				Draw.Line(this, "LStop" + longEntryBar, false, barsAgoStart, longStop, -ProjectionBars, longStop, Brushes.OrangeRed, DashStyleHelper.Dash, 1);
				if (longTarget != 0)
					Draw.Line(this, "LTarget" + longEntryBar, false, barsAgoStart, longTarget, -ProjectionBars, longTarget, Brushes.Lime, DashStyleHelper.Dash, 1);

				if (Low[0] <= longStop)
				{
					Draw.Text(this, "LStopHit" + longEntryBar, "SL", 0, longStop - (TickSize * 6), Brushes.OrangeRed);
					longActive = false;
				}
				else if (longTarget != 0 && High[0] >= longTarget)
				{
					Draw.Text(this, "LTargetHit" + longEntryBar, "TP", 0, longTarget + (TickSize * 6), Brushes.Lime);
					longActive = false;
				}
			}

			if (shortActive)
			{
				int barsAgoStart = CurrentBar - shortEntryBar;
				Draw.Line(this, "SStop" + shortEntryBar, false, barsAgoStart, shortStop, -ProjectionBars, shortStop, Brushes.OrangeRed, DashStyleHelper.Dash, 1);
				if (shortTarget != 0)
					Draw.Line(this, "STarget" + shortEntryBar, false, barsAgoStart, shortTarget, -ProjectionBars, shortTarget, Brushes.Red, DashStyleHelper.Dash, 1);

				if (High[0] >= shortStop)
				{
					Draw.Text(this, "SStopHit" + shortEntryBar, "SL", 0, shortStop + (TickSize * 6), Brushes.OrangeRed);
					shortActive = false;
				}
				else if (shortTarget != 0 && Low[0] <= shortTarget)
				{
					Draw.Text(this, "STargetHit" + shortEntryBar, "TP", 0, shortTarget - (TickSize * 6), Brushes.Red);
					shortActive = false;
				}
			}
		}

		// Same recursive RSX smoothing as before, but now fed exclusively by
		// closed bars (Input[1]/Input[2]) and computed once per bar from
		// UpdateBarStructure(). The result (rsxVal) stays fixed for the rest
		// of the bar instead of recalculating on every tick.
		private double CalculateRSX()
		{
			if (CurrentBar < 2) return 50;
			double f18 = 3.0 / (Rsx_len + 2.0);
			double f20 = 1.0 - f18;
			double v8 = (Input[1] - Input[2]) * 100;
			f28[0] = f20 * f28[1] + f18 * v8;
			f30[0] = f18 * f28[0] + f20 * f30[1];
			double vC = f28[0] * 1.5 - f30[0] * 0.5;
			f38[0] = f20 * f38[1] + f18 * vC;
			f40[0] = f18 * f38[0] + f20 * f40[1];
			double v10 = f38[0] * 1.5 - f40[0] * 0.5;
			f48[0] = f20 * f48[1] + f18 * v10;
			f50[0] = f18 * f48[0] + f20 * f50[1];
			double v14 = f48[0] * 1.5 - f50[0] * 0.5;
			f58[0] = f20 * f58[1] + f18 * Math.Abs(v8);
			f60[0] = f18 * f58[0] + f20 * f60[1];
			double v18 = f58[0] * 1.5 - f60[0] * 0.5;
			f68[0] = f20 * f68[1] + f18 * v18;
			f70[0] = f18 * f68[0] + f20 * f70[1];
			double v1C = f68[0] * 1.5 - f70[0] * 0.5;
			f78[0] = f20 * f78[1] + f18 * v1C;
			f80[0] = f18 * f78[0] + f20 * f80[1];
			double v20 = f78[0] * 1.5 - f80[0] * 0.5;
			return v20 > 0 ? (v14 / v20 + 1) * 50 : 50;
		}

		// Shifted by +1 vs. the original: strength+1 is the pivot candidate,
		// and the scan window (1 .. strength*2+1) never includes index 0 -
		// the live, still-forming bar. That's what stops a swing from being
		// "un-confirmed" mid-bar.
		private bool IsSwingHigh(int strength)
		{
			double pivotVal = High[strength + 1];
			for (int i = 1; i <= strength * 2 + 1; i++)
			{
				if (i == strength + 1) continue;
				if (High[i] >= pivotVal) return false;
			}
			return true;
		}

		private bool IsSwingLow(int strength)
		{
			double pivotVal = Low[strength + 1];
			for (int i = 1; i <= strength * 2 + 1; i++)
			{
				if (i == strength + 1) continue;
				if (Low[i] <= pivotVal) return false;
			}
			return true;
		}

		#region Properties
		[Browsable(false), XmlIgnore] public Series<double> BBUpper { get { return Values[0]; } }
		[Browsable(false), XmlIgnore] public Series<double> BBLower { get { return Values[1]; } }
		[Browsable(false), XmlIgnore] public Series<double> DayHigh { get { return Values[2]; } }
		[Browsable(false), XmlIgnore] public Series<double> DayLow { get { return Values[3]; } }
		[Browsable(false), XmlIgnore] public Series<double> Hma50Plot { get { return Values[4]; } }

		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="HMA Largo", GroupName="1. Parámetros", Order=0)]
		public int HmaL_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="HMA Corto", GroupName="1. Parámetros", Order=1)]
		public int HmaS_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="RSX Length", GroupName="1. Parámetros", Order=2)]
		public int Rsx_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="ATR Length", GroupName="1. Parámetros", Order=3)]
		public int Atr_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="BB Length", GroupName="1. Parámetros", Order=4)]
		public int BbLen { get; set; }
		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Min BB Width %", GroupName="1. Parámetros", Order=5)]
		public double MinBBW { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="Max Age Bars", GroupName="1. Parámetros", Order=6)]
		public int MaxAge { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="MACD MA", GroupName="1. Parámetros", Order=7)]
		public int Macd_ma { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="MACD Signal", GroupName="1. Parámetros", Order=8)]
		public int Macd_sig { get; set; }
		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Max Spread Ticks", GroupName="1. Parámetros", Order=9)]
		public double MaxSpread { get; set; }

		[NinjaScriptProperty, Range(0.1, 10.0), Display(Name="ATR Multiplier Arrow", GroupName="1. Parámetros", Order=10)]
		public double AtrMultiplier { get; set; }

		[NinjaScriptProperty, Display(Name="Use Anchor Trend", GroupName="1. Parámetros", Order=11)]
		public bool UseAnchor { get; set; }

		[NinjaScriptProperty, Display(Name="Usar Filtro de Tendencia HMA(50)", GroupName="1. Parámetros", Order=12)]
		public bool UseHma50Filter { get; set; }

		[NinjaScriptProperty, Display(Name="Usar Filtro de Estructura (Swing)", GroupName="2. Estructura Swing", Order=12)]
		public bool UseSwingFilter { get; set; }

		[NinjaScriptProperty, Range(1, 50), Display(Name="Fuerza del Swing (barras)", GroupName="2. Estructura Swing", Order=13)]
		public int SwingStrength { get; set; }

		[NinjaScriptProperty, Display(Name="Permitir Re-entrada en la misma tendencia", GroupName="2. Estructura Swing", Order=14)]
		public bool AllowReentry { get; set; }

		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Max Extension (x ATR desde rango diario)", GroupName="3. Gestión de Riesgo", Order=15)]
		public double MaxExtensionATR { get; set; }

		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Target Referencia (x ATR)", GroupName="3. Gestión de Riesgo", Order=16)]
		public double AtrTPMultiplier { get; set; }

		[NinjaScriptProperty, Range(1, 200), Display(Name="Barras de Proyección SL/TP", GroupName="3. Gestión de Riesgo", Order=17)]
		public int ProjectionBars { get; set; }

		[NinjaScriptProperty, Range(1, 20), Display(Name="Multiplicador de Ancla (x TF base)", GroupName="4. Multi-Temporalidad", Order=18)]
		public int AnchorMultiplier { get; set; }

		[NinjaScriptProperty, Range(50, 500), Display(Name="Barras de Calentamiento", GroupName="4. Multi-Temporalidad", Order=19)]
		public int WarmupBars { get; set; }

		[NinjaScriptProperty, Display(Name="Modo de Entrada", GroupName="5. Entradas de Impulso", Order=20)]
		public EntryModeType EntryMode { get; set; }

		[NinjaScriptProperty, Range(0, 6), Display(Name="Score Minimo para Impulso", GroupName="5. Entradas de Impulso", Order=21)]
		public int ImpulseScoreThreshold { get; set; }

		[NinjaScriptProperty, Display(Name="Exigir RSX en Extremo", GroupName="5. Entradas de Impulso", Order=22)]
		public bool RequireRsxExtreme { get; set; }

		[NinjaScriptProperty, Range(0, 100), Display(Name="RSX Sobreventa", GroupName="5. Entradas de Impulso", Order=23)]
		public double RsxOversold { get; set; }

		[NinjaScriptProperty, Range(0, 100), Display(Name="RSX Sobrecompra", GroupName="5. Entradas de Impulso", Order=24)]
		public double RsxOverbought { get; set; }
		#endregion
	}
}
