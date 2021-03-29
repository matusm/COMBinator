using System.Linq;
using Bev.IO.MenloReader;
using System.Threading;
using System.Globalization;
using System.IO;
using Bev.UI;


namespace COMBinator
{
    class MainClass
    {
        static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            var options = new Options();
            if (!CommandLine.Parser.Default.ParseArgumentsStrict(args, options))
                ConsoleUI.ErrorExit("*** ParseArgumentsStrict returned false", 1);

            #region Extract file names from the command line
            //TODO extension handling must be improved

            string sBaseFileName;
            string sInFileName = "";
            string sOutFileName = "";
            string sAuxFileName = "";

            string[] aFileNames = options.ListOfFileNames.ToArray();

            switch (aFileNames.Length)
            {
                case 1:
                    sBaseFileName = Path.ChangeExtension(aFileNames[0], null);
                    sOutFileName = sBaseFileName + ".prn";
                    sAuxFileName = sBaseFileName + ".txt";
                    sInFileName = sBaseFileName + ".dat";
                    break;
                case 2:
                    sBaseFileName = Path.ChangeExtension(aFileNames[0], null);
                    sOutFileName = Path.ChangeExtension(aFileNames[1], ".prn");
                    sAuxFileName = sBaseFileName + ".txt";
                    sInFileName = sBaseFileName + ".dat";
                    break;
                case 3:
                    sBaseFileName = Path.ChangeExtension(aFileNames[0], null);
                    sOutFileName = Path.ChangeExtension(aFileNames[1], ".prn");
                    sAuxFileName = Path.ChangeExtension(aFileNames[2], ".txt");
                    sInFileName = sBaseFileName + ".dat";
                    break;
                default:
                    ConsoleUI.ErrorExit(options.GetUsage(), 1);
                    break;
            }

            #endregion

            ConsoleUI.Verbatim = !options.Quiet;
            ConsoleUI.Welcome();

            #region Analyse options for provided information
            Specification specs = Specification.None;

            if (options.TargetFrequency != null && options.TargetFrequency > 0)
                specs = Specification.TargetOnly;

            if (options.ModeNumber > 0)
            {
                specs = Specification.ModeOnly;
                if (options.TargetFrequency != null && options.TargetFrequency > 0)
                    specs = Specification.TargetAndMode;
            }

            if (options.Bnb || options.Bno || options.Bpb || options.Bpo)
            {
                if (specs == Specification.ModeOnly) specs = Specification.ModeAndSigns;
                if (specs == Specification.TargetOnly) specs = Specification.TargetAndSigns;
                if (specs == Specification.TargetAndMode) specs = Specification.ModeAndSigns;
            }

            if (options.Auto633 == true)
                specs = Specification.Target633;

            if (specs == Specification.None || specs == Specification.ModeOnly)
                ConsoleUI.ErrorExit("Insufficient information for evaluation: " + specs.ToString(), 2);

            #endregion

            // interpret the command line option for comb type
            CombType combType = TranslateOptionCT(options.CombTypeNumber);

            // interpret the command line option for FXM number
            FxmNumber fxm = TranslateOptionFXM(options.FxmNumber);
            if (fxm == FxmNumber.Unknown) ConsoleUI.ErrorExit("FXM number (" + options.FxmNumber + ") invalid", 3);

            // create a new CombData object
            CombData combData = new CombData();
            // set coefficents for the voltage interpretation.
            // quick and dirty!!!!!
            // valid for a specific laser only!
            combData.SetCoefficients(13.29M, 3.2M, 2.0M);

            // fill combData object with data from a file
            // SetCoefficients() must be called in advance!
            ConsoleUI.ReadingFile(sInFileName);
            bool succ = combData.LoadFile(sInFileName);
            ConsoleUI.Done();
            if (!succ) ConsoleUI.ErrorExit("Error reading file", 4);

            // create a new CWBeatCalculation object with the provided comb type
            CwBeatCalculation calc = new CwBeatCalculation(combType);

            #region Filter raw data for outliers
            // create an outlier filter with the provided fxm number
            // This object is also a container for some parameters, thus must be constructed in any case
            OutlierFilter filter = new OutlierFilter(fxm);

            // the target offset frequency depends on the comb type and is accessible via the CwBeatCalculation object
            filter.TargetOffSet = calc.OffsetSetPoint;

            // the target repetition frequency is either set by options or predicted from the data
            if (options.FrepSetpoint == null)
                filter.TargetRepetitionRate = combData.PredictedFrep(filter.FxmCounter);
            else
            {
                if (options.FrepSetpoint == 0)
                    filter.TargetRepetitionRate = null;
                else
                    filter.TargetRepetitionRate = (decimal?)options.FrepSetpoint;
            }

            // if tolerance <= 0 do not check this criterion
            if (options.FrepTolerance <= 0)
                filter.ToleranceRepetitionRate = null;
            else
                filter.ToleranceRepetitionRate = (decimal)options.FrepTolerance;

            // if tolerance <= 0 do not check this criterion
            if (options.FoffTolerance <= 0)
                filter.ToleranceOffSet = null;
            else
                filter.ToleranceOffSet = (decimal)options.FoffTolerance;

            // if tolerance <= 0 do not check this criterion
            if (options.CycTolerance <= 0)
                filter.ToleranceCycleSlip = null;
            else
                filter.ToleranceCycleSlip = (decimal)options.CycTolerance;

            // filter only if nofilter option is unchecked
            if (!options.NoFilter)
            {
                combData.RemoveAllOutliers(filter);
            }
            #endregion

            // inform user about basic parameters
            if (combData.NumberFilteredDataPoint != null)
            {
                double all = (double)combData.NumberRawDataPoint;
                double fil = (double)combData.NumberFilteredDataPoint;
                ConsoleUI.WriteLine(string.Format("{0} data points read, {1} outliers removed ({2:F1} %).", all, all - fil, 100.0 * (all - fil) / all));
                if (fil == 0) ConsoleUI.ErrorExit("No data to process!", 5);
            }
            else
                ConsoleUI.WriteLine(string.Format("{0} data points read.", combData.NumberRawDataPoint));


            // perform calculation of laser frequency (if told to do so)
            CclHFS hfs = CclHFS.None;
            decimal? targetFrequency = null;
            decimal meanBeatFrequency = 0;
            decimal f_synth = 0;
            if (filter.FxmCounter == FxmNumber.Fxm0) meanBeatFrequency = combData.Counter2mean;
            if (filter.FxmCounter == FxmNumber.Fxm1) meanBeatFrequency = combData.Counter6mean;
            if (filter.TargetRepetitionRate == null)
            {
                if (filter.FxmCounter == FxmNumber.Fxm0) f_synth = combData.Counter0mean;
                if (filter.FxmCounter == FxmNumber.Fxm1) f_synth = combData.Counter4mean;
            }
            else
                f_synth = (decimal)filter.TargetRepetitionRate;
            if (options.Bpb) calc.SignBeat = +1.0m;
            if (options.Bnb) calc.SignBeat = -1.0m;
            if (options.Bpo) calc.SignOff = +1.0m;
            if (options.Bno) calc.SignOff = -1.0m;

            switch (specs)
            {
                case Specification.None:
                    // this should not happen!
                    break;
                case Specification.ModeAndSigns:
                    calc.ModeNumber = options.ModeNumber;
                    ConsoleUI.WriteLine("Manual mode. Mode number and signs provided.");
                    break;
                case Specification.ModeOnly:
                    // this should not happen!
                    calc.ModeNumber = options.ModeNumber;
                    // signs are set per default to +/+
                    break;
                case Specification.TargetAndSigns:
                    targetFrequency = (decimal)options.TargetFrequency;
                    calc.PredictModeNumber((decimal)targetFrequency, f_synth, meanBeatFrequency);
                    ConsoleUI.WriteLine("Semi-automatic mode for given target frequency and beat signs.");
                    break;
                case Specification.TargetAndMode:
                    targetFrequency = (decimal)options.TargetFrequency;
                    calc.ModeNumber = options.ModeNumber;
                    calc.PredictSigns((decimal)targetFrequency, f_synth, meanBeatFrequency);
                    ConsoleUI.WriteLine("Semi-automatic mode for given target frequency and mode number."); break;
                case Specification.TargetOnly:
                    targetFrequency = (decimal)options.TargetFrequency;
                    calc.PredictModeAndSigns((decimal)targetFrequency, f_synth, meanBeatFrequency);
                    ConsoleUI.WriteLine("Automatic mode for given target frequency.");
                    break;
                case Specification.Target633:
                    hfs = calc.PredictModeAndSigns633Hfs(f_synth, meanBeatFrequency);
                    ConsoleUI.WriteLine("Automatic mode for 633 nm, estimated HFS: " + hfs);
                    targetFrequency = CwCcl.Frequency(hfs);
                    break;
                default:
                    // this should not happen!
                    break;
            }

            // prepare the list of result frequencies etc.
            CombResult cr = new CombResult(combData, calc, filter.FxmCounter, targetFrequency, f_synth);

            #region Write aux file
            // AuxData must be initialized before call to TranslateAuxType()!
            AuxData auxDataList;
            if (!options.NoAuxFile)
            {
                auxDataList = new AuxData(cr.XDataForPlot, cr.YDataForPlot);
                ConsoleUI.WritingFile(sAuxFileName);
                StreamWriter hFileAux = new StreamWriter(sAuxFileName);
                foreach (var ad in auxDataList.AuxPods)
                {
                    decimal y = ad.Frequency;
                    if (options.Mjd)
                    {
                        decimal x = GetMjdFromUnix(combData.UnixSystemTime + ad.LogTime);
                        hFileAux.WriteLine("{0,15:F7} {1,23:F3}", x, y);
                    }
                    else
                    {
                        decimal x = ad.LogTime;
                        hFileAux.WriteLine("{0,11:F3} {1,23:F3}", x, y);
                    }
                }
                hFileAux.Close();
                ConsoleUI.Done();
            }
            #endregion

            #region Output to main file
            ConsoleUI.WritingFile(sOutFileName);
            StreamWriter hFile = new StreamWriter(sOutFileName);
            // write file header
            if (!options.NoHeader)
            {
                hFile.WriteLine(ConsoleUI.WelcomeMessage);
                hFile.WriteLine();
                hFile.WriteLine("Header of input file {0}:", sInFileName);
                foreach (string s in combData.InputFileHeaders) hFile.WriteLine(" > " + s);
                hFile.WriteLine();
                if (options.Comment != "")
                {
                    hFile.WriteLine("User supplied comment:");
                    hFile.WriteLine("   " + options.Comment);
                    hFile.WriteLine();
                }
                hFile.WriteLine("Parameters:");
                hFile.WriteLine("   User supplied specifications: {0}", specs);
                hFile.WriteLine("   Total number of data points: {0}", combData.NumberRawDataPoint);
                hFile.WriteLine("   Comb generator type: {0}", calc.CombDescription);
                hFile.WriteLine("   FXM counter #{0}", options.FxmNumber);
                hFile.WriteLine("   Predicted IF: {0:F3} Hz", (decimal)combData.PredictedFrep(filter.FxmCounter));
                if (targetFrequency != null) hFile.WriteLine("   Target frequency: {0:F0} Hz", targetFrequency);
                if (hfs != CclHFS.None) hFile.WriteLine("   CCL HFS component: {0}", hfs);
                hFile.WriteLine("   Mode number: {0}", calc.ModeNumber);
                hFile.WriteLine("   Offset frequency sign: {0}", (calc.SignOff > 0) ? "positive" : "negative");
                hFile.WriteLine("   Beat frequency sign: {0}", (calc.SignBeat > 0) ? "positive" : "negative");
                hFile.WriteLine();
                if (!options.NoFilter)
                {
                    hFile.WriteLine("Outlier filter specification:");
                    if (filter.TargetRepetitionRate != null) hFile.WriteLine("   Filtering f_rep (IF) for {0:F3} Hz +/- {1:F3} Hz", filter.TargetRepetitionRate, filter.ToleranceRepetitionRate);
                    if (filter.TargetOffSet != null) hFile.WriteLine("   Filtering f_off (fundamental) for {0:F3} Hz +/- {1:F3} Hz", filter.TargetOffSet, filter.ToleranceOffSet);
                    if (filter.ToleranceCycleSlip != null) hFile.WriteLine("   Cycle slip detection of f_beat +/- {0:F3} Hz", filter.ToleranceCycleSlip);
                    hFile.WriteLine("   Total number of outliers: {0}", combData.NumberRawDataPoint - combData.NumberFilteredDataPoint);
                    hFile.WriteLine("   Number of f_rep outliers: {0}", combData.OutlierFrep(filter.FxmCounter));
                    hFile.WriteLine("   Number of f_off outliers: {0}", combData.OutlierFoff(filter.FxmCounter));
                    hFile.WriteLine("   Number of cycle slips: {0}", combData.OutlierCycl(filter.FxmCounter));
                    hFile.WriteLine();
                }
                if (!options.NoAuxFile)
                {
                    hFile.WriteLine("Auxiliary output file:");
                    hFile.WriteLine("   File name: " + sAuxFileName);
                    hFile.WriteLine("   Ordinate: " + TranslateAuxType(cr.AuxType));
                    hFile.WriteLine();
                }
                hFile.WriteLine("Statistics: [ Quantity  Average  StdDev  Span ]");
                foreach (var s in cr.Statistics) hFile.WriteLine("   " + s); // cr.Satatistics is a lengthy operation!
                hFile.WriteLine();

                hFile.WriteLine("Meaning of columns:");
                foreach (var s in cr.ColumnHeaders) hFile.WriteLine("   " + s);
                hFile.WriteLine();
                hFile.WriteLine("@@@@");    // legacy separator
            }

            //write actual data
            foreach (var obj in cr.ResultData) { hFile.WriteLine(obj); }
            hFile.Close();
            ConsoleUI.Done();
            #endregion
        }

        #region Helper functions

        /// <summary>
        /// Get MJD from UNIX time
        /// </summary>
        static decimal GetMjdFromUnix(decimal unixSeconds)
        {
            return (unixSeconds + 2209161600.0m) / 86400.0m + 15018.0m;
        }

        static string TranslateAuxType(PlotType at)
        {
            string str = "";
            switch (at)
            {
                case PlotType.None:
                    str = "None";
                    break;
                case PlotType.DeltaLaserFrequencyFixed:
                    str = "Laser frequency relative to target; using set repetition rate.";
                    break;
                case PlotType.DeltaLaserFrequency:
                    str = "Laser frequency relative to target; using measured repetition rate.";
                    break;
                case PlotType.LaserFrequencyFixed:
                    str = "Laser frequency; using set repetition rate.";
                    break;
                case PlotType.LaserFrequency:
                    str = "Laser frequency; using set measured rate.";
                    break;
                default:
                    str = "None";
                    break;
            }
            return str;
        }

        /// <summary>
        /// Translates the integer value provided via the command line option to a <c>CombType</c> value.
        /// </summary>
        /// <param name="opt">The option value.</param>
        /// <returns>The <c>CombType</c>.</returns>
        static CombType TranslateOptionCT(int opt)
        {
            switch (opt)
            {
                case 1:
                    return CombType.BevFiberShg;
                case 2:
                    return CombType.BevFiber;
                case 3:
                    return CombType.BevTiSa;
                case 4:
                    return CombType.CmiTiSa;
                case 5:
                    return CombType.BevUln;
                case 6:
                    return CombType.BevUlnShg;
                default:
                    return CombType.Generic;
            }
        }

        /// <summary>
        /// Translates the integer value provided via the command line option to a <c>FxmNumber</c> value.
        /// </summary>
        /// <param name="opt">The option value.</param>
        /// <returns>The <c>FxmNumber</c>.</returns>
        static FxmNumber TranslateOptionFXM(int opt)
        {
            switch (opt)
            {
                case 1:
                    return FxmNumber.Fxm0;
                case 2:
                    return FxmNumber.Fxm1;
                default:
                    return FxmNumber.Unknown;
            }
        }
        #endregion

    }
}
