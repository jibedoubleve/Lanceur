﻿using Probel.Lanceur.Core.Services;
using Unity;

namespace Probel.Lanceur.Actions
{
    [UiAction]
    public class ImportAction : BaseUiAction
    {
        #region Fields

        private ISlickRunImporterService _importer;
        private ISettingsService _settingsService;

        #endregion Fields

        #region Properties

        private ISlickRunImporterService Importer
        {
            get
            {
                if (_importer == null) { _importer = Container.Resolve<ISlickRunImporterService>(); }
                return _importer;
            }
        }

        private ISettingsService SettingsService
        {
            get
            {
                if (_settingsService == null) { _settingsService = Container.Resolve<ISettingsService>(); }
                return _settingsService;
            }
        }

        #endregion Properties

        #region Methods

        public override void Execute(string arg)
        {
            var sessionId = Importer.Import();
            //Now update the settings
            var s = SettingsService.Get();
            s.SessionId = sessionId;
            SettingsService.Save(s);
        }

        #endregion Methods
    }
}