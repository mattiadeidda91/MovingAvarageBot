using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None)]
    public class MovingAvarageBot : Robot
    {
        private MovingAverage ema;
        private MovingAverage ma;
        private MovingAverage maSlow;
        private double volumeInUnits;
        private int coutConsecutiveLoss = 0;
        private bool alreadyTrailing = false;
        private Dictionary<int, bool> dic_positions { get; set; } = new();

        //Parameters for bouncing strategy
        [Parameter("Periods", DefaultValue = 100, Group = "Trade - Bouncing (RSI)")]
        public int Periods { get; set; }
        private RelativeStrengthIndex rsi;

        /*  TRADE PARAMETERS */

        [Parameter("Volume (Lots)", DefaultValue = 0.01, Group = "Trade MovingAvarage")]
        public double volumeInLots { get; set; }

        [Parameter("Stop Loss (Pips)", DefaultValue = 10, Group = "Trade MovingAvarage")]
        public double StopLossInPips { get; set; }

        [Parameter("Take Profit (Pips)", DefaultValue = 10, Group = "Trade MovingAvarage")]
        public double TakeProfitInPips { get; set; }

        [Parameter("Loss Pips for Inverting", DefaultValue = -20, Group = "Trade MovingAvarage")]
        public double LossPips { get; set; }

        [Parameter("Max Consecutive Loss", DefaultValue = 4, Group = "Trade MovingAvarage", Step = 1)]
        public double MaxConsecutiveLoss { get; set; }

        /* TRAILING PARAMETERS */

        [Parameter("Enable Trailing", DefaultValue = false, Group = "Trailing")]
        public bool EnableTrailing { get; set; }

        [Parameter("Trailing Profit Trigger (Pips)", DefaultValue = 20, Group = "Trailing")]
        public double TrailingProfitTrigger { get; set; }

        [Parameter("Trailing Stop Trigger (Pips)", Group = "Trailing", DefaultValue = 20)]
        public double TrailingStopTrigger { get; set; }

        [Parameter("Trailing Stop Step (Pips)", Group = "Trailing", DefaultValue = 10)]
        public double TrailingStopStep { get; set; }

        [Parameter("Trailing Profit Step (Pips)", Group = "Trailing", DefaultValue = 10)]
        public double TrailingProfitStep { get; set; }

        /* OTHER PARAMETERS */
        [Parameter("Moving Avarage Crossed Angle", Group = "Other", DefaultValue = 20)]
        public double MovingAvarageCrossedAngle { get; set; }

        protected override void OnStart()
        {
            Positions.Closed += OnPositionClosed;

            volumeInUnits = Symbol.QuantityToVolumeInUnits(volumeInLots);

            ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 9);
            ma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 21);
            maSlow = Indicators.SimpleMovingAverage(Bars.ClosePrices, 200);

            rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
        }

        protected override void OnBar()
        {
            if (!Positions.FindAll("Trade").Any())
            {
                alreadyTrailing = false;

                StrategyTrend();
                //StrategyBouncing();
            }
            else
            {
                /*
                 * Gestione del trade
                 * Idee:
                 * 1. Se si ha un tot di perdita, invertire posizione trade, provando a non gestire lo stop loss
                 * 2. Valutare di inserire un trailing stop e/o trailing profit
                 * 3. Valutare di inserire un break even
                */

                var position = Positions.FindAll("Trade").FirstOrDefault();

                ////Inversione della position se la perdita è superiore al numero di Pips passato
                //InversionPosition(position);

                //if (EnableTrailing && !alreadyTrailing)
                //    TrailingProfit(position);

                if (EnableTrailing && dic_positions.ContainsKey(position!.Id))
                {
                    _ = dic_positions.TryGetValue(position.Id, out var temp_isFirstTraling);

                    TrailingStop(position, temp_isFirstTraling);
                }
            }
        }

        protected override void OnTick()
        {
            if (Positions.FindAll("Trade").Any())
            {
                var position = Positions.FindAll("Trade").FirstOrDefault();

                //Inversione della position se la perdita è superiore al numero di Pips passato
                InversionPosition(position);
            }
        }

        private void StrategyTrend()
        {
            /* 
             * Best optimize:
             * EUR/USD
             * Period: 08/02/2011 - 09/03/2023
             * TimeFrame: 15min
             * Lot : 0.1
             * Stop: 0
             * Take: 45
             * Loss Pips: -45
             * Inverted Positions: true
             * MaxConsecutiveLoss: Senza gestione
             * EnableTrailing: false
             * Profit: 6626.71 (+663%)
             */

            /* 
             * Best optimize:
             * EUR/USD
             * Period: 08/02/2011 - 09/03/2023
             * TimeFrame: 15min
             * Lot : 0.1
             * Stop: 0
             * Take: 45
             * Loss Pips: -45
             * Inverted Positions: true
             * MaxConsecutiveLoss: 4
             * EnableTrailing: false
             * Profit: 7.788.15 (+779%)
             */

            /* 
             * DA RIVEDERE SICCOME NON STAVA PARTENDO IL TRAILING
             * Best optimize:
             * EUR/USD
             * Period: 08/02/2011 - 09/03/2023
             * TimeFrame: 15min
             * Lot : 0.1
             * Stop: 0
             * Take: 45
             * Loss Pips: -45
             * Inverted Positions: true
             * MaxConsecutiveLoss: 4
             * EnableTrailing: true -> TrailingProfit()
             * TrailingTrigger: 40
             * TrailingStep: 25 -> in realtà 10
             * Profit: 7.202.67 (+720%)
             */

            if (ema.Result.HasCrossedAbove(ma.Result, 0) && 
                TrendByMA() != Trend.Lateral && 
                CalculateAngle(TradeType.Buy) > MovingAvarageCrossedAngle) //&& TrendByMA() == Trend.Bullish
            {
                //Incrocia da sotto a sopra
                ClosePositions(TradeType.Sell);
                var pos = ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);

                dic_positions.TryAdd(pos.Position.Id, true);
            }
            else if (ema.Result.HasCrossedBelow(ma.Result, 0) && 
                TrendByMA() != Trend.Lateral && 
                CalculateAngle(TradeType.Sell) < -MovingAvarageCrossedAngle) //&& TrendByMA() == Trend.Bearish
            {
                //Incrocia da sopra a sotto
                ClosePositions(TradeType.Buy);
                var pos = ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);

                dic_positions.TryAdd(pos.Position.Id, true);
            }
        }

        private void StrategyBouncing()
        {
            //double rsiValue = rsi.Result.LastValue;
            //if(rsiValue >= 75 && Bars.ClosePrices.LastValue >= Bars.HighPrices.LastValue)
            //{
            //    ClosePositions(TradeType.Sell);
            //    ClosePositions(TradeType.Buy);
            //    ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);
            //}
            //else if(rsiValue <= 25 && Bars.ClosePrices.LastValue <= Bars.LowPrices.LastValue)
            //{
            //    ClosePositions(TradeType.Sell);
            //    ClosePositions(TradeType.Buy);
            //    ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);
            //}


            /* 
             * Best optimize:
             * EUR/USD
             * Period: 1/1/2011 - 1/1/2023
             * TimeFrame: 15min
             * Lot : 0.1
             * Stop: 30
             * Take: 30
             * Inverted Positions: false
             * Profit: 500
             */
            var currentBarIndex = Bars.ClosePrices.Count - 1;

            // Calcola i livelli di supporto e resistenza
            var high = Bars.HighPrices.Maximum(Periods);
            var low = Bars.LowPrices.Minimum(Periods);

            // Verifica se il prezzo sta toccando il livello di supporto o resistenza
            if (Bars.ClosePrices[currentBarIndex] >= high)
            {
                ClosePositions(TradeType.Sell);
                ClosePositions(TradeType.Buy);
                ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);
            }
            else if (Bars.ClosePrices[currentBarIndex] <= low)
            {
                ClosePositions(TradeType.Sell);
                ClosePositions(TradeType.Buy);
                ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);
            }
        }

        private void TrailingStop(Position position, bool isFirstTralingStop)
        {
            double? marginProfit;
            double? averageMarginProfit;
            double? NewStopLoss;
            double? NewTakeProfit;

            if (position != null)
            {
                if (position.TradeType == TradeType.Buy)
                {
                    if (isFirstTralingStop)
                    {
                        marginProfit = position.TakeProfit - position.EntryPrice;
                        averageMarginProfit = position.EntryPrice + (marginProfit / 2);
                        if (Symbol.Bid >= averageMarginProfit)
                        {
                            NewStopLoss = position.EntryPrice + TrailingStopStep * Symbol.PipSize;
                            ModifyPosition(position, NewStopLoss, position.TakeProfit);

                            dic_positions[position.Id] = false;
                            Print("Traling Stop Buy - New SL at: ", NewStopLoss);
                        }
                    }
                    else
                    {
                        marginProfit = position.TakeProfit - position.StopLoss;
                        averageMarginProfit = position.StopLoss + (marginProfit / 2);
                        if (Symbol.Bid >= averageMarginProfit)
                        {
                            NewStopLoss = position.StopLoss + (TrailingStopStep / 2) * Symbol.PipSize;
                            NewTakeProfit = position.TakeProfit + TrailingProfitStep * Symbol.PipSize;

                            ModifyPosition(position, NewStopLoss, NewTakeProfit);
                            dic_positions[position.Id] = false;
                            Print("NEW Traling Stop Buy - New SL at: ", NewStopLoss, " and New TP at: ", NewTakeProfit);
                        }
                    }
                }
                else if (position.TradeType == TradeType.Sell)
                {
                    if (isFirstTralingStop)
                    {
                        marginProfit = position.EntryPrice - position.TakeProfit;
                        averageMarginProfit = position.EntryPrice - (marginProfit / 2);
                        if (Symbol.Ask <= averageMarginProfit)
                        {
                            NewStopLoss = position.EntryPrice - TrailingStopStep * Symbol.PipSize;
                            ModifyPosition(position, NewStopLoss, position.TakeProfit);

                            dic_positions[position.Id] = false;
                            Print("Traling Stop Sell - New SL at: ", NewStopLoss);
                        }
                    }
                    else
                    {
                        marginProfit = position.StopLoss - position.TakeProfit;
                        averageMarginProfit = position.StopLoss - (marginProfit / 2);
                        if (Symbol.Ask <= averageMarginProfit)
                        {
                            NewStopLoss = position.StopLoss - (TrailingStopStep / 2) * Symbol.PipSize;
                            NewTakeProfit = position.TakeProfit - TrailingProfitStep * Symbol.PipSize;

                            ModifyPosition(position, NewStopLoss, NewTakeProfit);
                            dic_positions[position.Id] = false;
                            Print("NEW Traling Stop Sell - New SL at: ", NewStopLoss, " and New TP at: ", NewTakeProfit);
                        }
                    }
                }
            }
        }

        private void TrailingProfit(Position position)
        {
            var tradeType = position.TradeType;
            var netProfit = position.NetProfit;
            var pipsProfit = position.Pips;
            var isProfit = netProfit > 0;
            TradeResult pos;

            if (isProfit && tradeType == TradeType.Sell)
            {
                if (pipsProfit > TrailingProfitTrigger)
                {
                    double newStopLossPrice = Symbol.Ask + TrailingStopStep * Symbol.PipSize;

                    if (position.StopLoss == null || newStopLossPrice < position.StopLoss)
                    {
                        pos = ModifyPosition(position, position.TakeProfit + TrailingStopStep * Symbol.PipSize, position.TakeProfit - TrailingStopStep * Symbol.PipSize);
                        
                        if (pos.IsSuccessful) 
                            alreadyTrailing = true;
                        else
                            Print("Success: {0} - Error: {1}", pos.IsSuccessful, pos.Error.GetValueOrDefault());
                    }
                }
            }
            else if (isProfit && tradeType == TradeType.Buy)
            {
                if (pipsProfit > TrailingProfitTrigger)
                {
                    double newStopLossPrice = Symbol.Bid - TrailingStopStep * Symbol.PipSize;

                    if (position.StopLoss == null || newStopLossPrice > position.StopLoss)
                    {
                        pos = ModifyPosition(position, position.TakeProfit - TrailingStopStep * Symbol.PipSize, position.TakeProfit + TrailingStopStep * Symbol.PipSize);
                        
                        if (pos.IsSuccessful)
                            alreadyTrailing = true;
                        else
                            Print("Success: {0} - Error: {1}", pos.IsSuccessful, pos.Error.GetValueOrDefault());
                    }
                }
            }
        }

        private void InversionPosition(Position position)
        {
            var tradeType = position?.TradeType;
            var netProfit = position.NetProfit;
            var pipsProfit = position.Pips;
            var isProfit = netProfit > 0;

            if (!isProfit && pipsProfit < LossPips)
            {
                coutConsecutiveLoss++;
                if (coutConsecutiveLoss >= MaxConsecutiveLoss) // && TrendByMA() != Trend.Lateral
                {
                    ClosePositions(TradeType.Buy);
                    ClosePositions(TradeType.Sell);
                    coutConsecutiveLoss = 0;
                }
                else
                {
                    if (tradeType == TradeType.Buy)
                    {
                        ClosePositions(TradeType.Buy);
                        var pos = ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);

                        dic_positions.TryAdd(pos.Position.Id, true);
                    }
                    else if (tradeType == TradeType.Sell)
                    {
                        ClosePositions(TradeType.Sell);
                        var pos = ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, "Trade", StopLossInPips, TakeProfitInPips);

                        dic_positions.TryAdd(pos.Position.Id, true);
                    }
                }
            }
        }

        private Trend TrendByMA()
        {
            var currentPrice = Bars.ClosePrices.LastValue;
            var maValue = maSlow.Result.LastValue;

            if (currentPrice > maValue)
                return Trend.Bullish;
            else if (currentPrice < maValue)
                return Trend.Bearish;
            else
                return Trend.Lateral;
        }

        protected override void OnStop()
        {
            Positions.Closed -= OnPositionClosed;

            ClosePositions(TradeType.Buy);
            ClosePositions(TradeType.Sell);
        }

        private double CalculateAngle(TradeType trade)
        {
            double crossAngle = Math.Atan((ema.Result.LastValue - ma.Result.LastValue) / Symbol.PipSize) * 180 / Math.PI;

            Print("Angolo di incrocio2: {0} di tipo: {1} at price: {2} at Time: {3}", crossAngle, trade.ToString(), Bars.ClosePrices.LastValue, Bars.Last().OpenTime);

            return crossAngle;
        }       

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            // Esegui qui la logica per gestire l'evento di trade chiuso
            if (args.Position.Label == "Trade")
            {
                dic_positions.Remove(args.Position.Id);
            }
        }

        private void ClosePositions(TradeType tradeType)
        {
            foreach (var position in Positions.FindAll("Trade"))
            {
                if (position.TradeType != tradeType) continue;

                ClosePosition(position);

                //if (pos.IsSuccessful) dic_positions.Remove(position.Id);
            }
        }

        public enum Trend
        {
            Bullish,
            Bearish,
            Lateral,
            None
        }
    }
}