﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using TwainWeb.Standalone.App.Extensions;
using TwainWeb.Standalone.App.Scanner;
using TwainWeb.Standalone.App.Twain;
using WIA;

namespace TwainWeb.Standalone.App.Wia
{
	class WiaSource:ISource
	{
		#region constants
		class WIA_DPS_DOCUMENT_HANDLING_SELECT
		{
			public const uint FEEDER = 0x00000001;
			public const uint FLATBED = 0x00000002;
		}

		class WIA_DPS_DOCUMENT_HANDLING_STATUS
		{
			public const uint FEED_READY = 0x00000001;
		}

		class WIA_PROPERTIES
		{
			private const uint WIA_RESERVED_FOR_NEW_PROPS = 1024;
			private const uint WIA_DIP_FIRST = 2;
			private const uint WIA_DPA_FIRST = WIA_DIP_FIRST + WIA_RESERVED_FOR_NEW_PROPS;
			private const uint WIA_DPC_FIRST = WIA_DPA_FIRST + WIA_RESERVED_FOR_NEW_PROPS;
			//
			// Scanner only device properties (DPS)
			//
			private const uint WIA_DPS_FIRST = WIA_DPC_FIRST + WIA_RESERVED_FOR_NEW_PROPS;
			public const uint WIA_DPS_DOCUMENT_HANDLING_STATUS = WIA_DPS_FIRST + 13;
			public const uint WIA_DPS_DOCUMENT_HANDLING_SELECT = WIA_DPS_FIRST + 14;
		}
		#endregion

		private readonly string _deviceId;
		private readonly int _sourceIndex;
		private readonly string _name;

		public WiaSource(int index, string name, string deviceId)
		{
			_sourceIndex = index;
			_name = name;
			_deviceId = deviceId;
			
			//ConnectToDevice(deviceInfo.DeviceID);
		}
		public string DeviceId { get { return _deviceId; } }
		public string Name { get { return _name; } }
		public int Index { get { return _sourceIndex; } }


		public ScannerSettings GetScannerSettings()
		{
			if (_deviceId == null)
			{
				throw new Exception("Не выбран источник данных для сканера.");
			}

			// connect to scanner
			var device = ConnectToDevice();
			var source = device.Items[1];

			var settings = new ScannerSettings(
				_sourceIndex,
				_name,
				GetAllowableResolutions(source),
				GetAllowablePixelTypes(),
				GetMaxHeight(device),
				GetMaxWidth(device));

			return settings;
		}

		/// <summary>
		/// Use scanner to scan an image (with user selecting the scanner from a dialog).
		/// </summary>
		/// <returns>Scanned images.</returns>
		public List<Image> Scan(SettingsAcquire settings)
		{

			if (_deviceId == null)
			{
				throw new Exception("Не выбран источник данных для сканера.");
			}

			var device = ConnectToDevice();
			SetAcquireSettings(device, settings);

			return Scan(device);
		}


		/// <summary>
		/// Use scanner to scan an image (scanner is selected by its unique id).
		/// </summary>
		/// <returns>Scanned images.</returns>
		private List<Image> Scan(Device device)
		{
		
			var images = new List<Image>();

			var hasMorePages = true;
			while (hasMorePages)
			{

				var item = device.Items[1];;

				try
				{
					// scan image
					var image = (ImageFile)item.Transfer(FormatID.wiaFormatBMP);

					if (image == null) throw new Exception("Не удалось отсканировать изображение");

					// save to memory stream
					var buffer = (byte[])image.FileData.get_BinaryData();
					var stream = new MemoryStream(buffer);
					var img = Image.FromStream(stream);

					images.Add(img);

				}
				catch (Exception exc)
				{
					throw exc;
				}
				finally
				{
					//determine if there are any more pages waiting
					Property documentHandlingSelect = null;
					Property documentHandlingStatus = null;

					foreach (Property prop in device.Properties)
					{
						if (prop.PropertyID == WIA_PROPERTIES.WIA_DPS_DOCUMENT_HANDLING_SELECT)
							documentHandlingSelect = prop;

						if (prop.PropertyID == WIA_PROPERTIES.WIA_DPS_DOCUMENT_HANDLING_STATUS)
							documentHandlingStatus = prop;
					}

					// assume there are no more pages
					hasMorePages = false;

					// may not exist on flatbed scanner but required for feeder
					if (documentHandlingSelect != null)
					{
						// check for document feeder
						if ((Convert.ToUInt32(documentHandlingSelect.get_Value()) & WIA_DPS_DOCUMENT_HANDLING_SELECT.FEEDER) != 0)
						{
							hasMorePages = ((Convert.ToUInt32(documentHandlingStatus.get_Value()) & WIA_DPS_DOCUMENT_HANDLING_STATUS.FEED_READY) != 0);
						}
					}
				}
			}

			return images;
		}

		private DeviceInfo DeviceInfo
		{
			get
			{
				var manager = new DeviceManager();
				// select the correct scanner using the provided scannerId parameter
				foreach (DeviceInfo info in manager.DeviceInfos)
				{
					if (info.DeviceID == _deviceId)
					{
						return info;
					}
				}
				return null;
			}
		}

		private Device ConnectToDevice()
		{
			
			// connect to scanner
			var device = DeviceInfo.Connect();
			
			if (device == null)
			{
				// show error
				throw new Exception("The device with provided ID could not be found.");
			}

			if (device.Items.Count == 0)
				throw new Exception("The device hasn't any device info");

			return device;
		}

		private float GetMaxHeight(IDevice device)
		{
			var verticalBedSize = FindProperty(device.Properties, WiaProperty.VerticalBedSize);
			var vertical = verticalBedSize.get_Value();
			var maxHeight = (float)(int)vertical / 1000;
			return maxHeight;
		}

		private float GetMaxWidth(IDevice device)
		{
			var horizontalBedSize = FindProperty(device.Properties, WiaProperty.HorizontalBedSize);
			var horizontal = horizontalBedSize.get_Value();
			var maxWidth = (float)(int)horizontal / 1000;
			return maxWidth;
		}

		private void SetAcquireSettings(Device device, SettingsAcquire settings)
		{
			var source = device.Items[1];
			if (source == null) throw new Exception("Current sourse not found");

			SetProperty(source.Properties, WiaProperty.HorizontalResolution, (int)settings.Resolution);
			SetProperty(source.Properties, WiaProperty.VerticalResolution, (int)settings.Resolution);

			var horizontalExtent = (int)(settings.Format.Width * settings.Resolution);
			var verticalExtent = (int)(settings.Format.Height * settings.Resolution);

			var horizontalExtentMax = FindProperty(source.Properties, WiaProperty.HorizontalExtent).SubTypeMax;
			var verticalExtentMax = FindProperty(source.Properties, WiaProperty.VerticalExtent).SubTypeMax;

			var currentIntent = (WiaPixelType)settings.PixelType;

			SetProperty(source.Properties, WiaProperty.HorizontalExtent, horizontalExtent < horizontalExtentMax ? horizontalExtent : horizontalExtentMax);
			SetProperty(source.Properties, WiaProperty.VerticalExtent, verticalExtent < verticalExtentMax ? verticalExtent : verticalExtentMax);
			SetProperty(source.Properties, WiaProperty.CurrentIntent, currentIntent);

			if (currentIntent == WiaPixelType.Color)
				try
				{
					SetProperty(source.Properties, WiaProperty.BitsPerPixel, 24);
				}
				catch (Exception)
				{
				}
		}

		private Dictionary<int, string> GetAllowablePixelTypes()
		{
			var pixelTypes = new Dictionary<int, string>();
			foreach (WiaPixelType pixelType in Enum.GetValues(typeof(WiaPixelType)))
			{
				pixelTypes.Add((int)pixelType, EnumExtensions.GetDescription(pixelType));
			}

			return pixelTypes;
		}

		private List<float> GetAllowableResolutions(IItem source)
		{
			var verticalResolution = FindProperty(source.Properties, WiaProperty.VerticalResolution);
			var horizontalResolution = FindProperty(source.Properties, WiaProperty.HorizontalResolution);

			if (verticalResolution == null || horizontalResolution == null) throw new Exception("Не удалось получить допустимые разрешения сканера");

			var verticalResolutions = new List<float>();
			var horizontalResolutions = new List<float>();

			Vector verticalResolutionsVector = null;
			Vector horizontalResolutionsVector = null;

			//Разрешения могут быть представлены либо списком в SubTypeValues, либо минимальным, максимальным значаниями и шагом (SubTypeMin, SubTypeMax, SubTypeStep)
			var isVector = false;

			try
			{
				verticalResolutionsVector = verticalResolution.SubTypeValues;
				horizontalResolutionsVector = horizontalResolution.SubTypeValues;

				isVector = true;
			}
			catch (Exception)
			{
				isVector = false;
			}

			if (isVector)
			{
				foreach (var hResolution in horizontalResolutionsVector)
				{
					horizontalResolutions.Add((int)hResolution);
				}

				foreach (var vResolution in verticalResolutionsVector)
				{
					verticalResolutions.Add((int)vResolution);
				}
			}
			else
				try
				{
					for (var i = verticalResolution.SubTypeMin;
						i <= verticalResolution.SubTypeMax;
						i += verticalResolution.SubTypeStep)
					{
						verticalResolutions.Add(i);
					}

					for (var i = horizontalResolution.SubTypeMin;
						i <= horizontalResolution.SubTypeMax;
						i += horizontalResolution.SubTypeStep)
					{
						horizontalResolutions.Add(i);
					}
				}
				catch (Exception)
				{
					throw new Exception("Не удалось получить допустимые разрешения сканера");
				}

			return horizontalResolutions.Count < verticalResolutions.Count
				? horizontalResolutions
				: verticalResolutions;
		}



		private static Property FindProperty(WIA.Properties properties, WiaProperty property)
		{
			foreach (Property prop in properties)
			{
				if (prop.PropertyID == (int)property)
				{
					return prop;
				}
			}
			return null;
		}

		private void SetProperty(IProperties properties, WiaProperty property, object propValue)
		{
			var propName = ((int)property).ToString();
			var prop = properties.get_Item(propName);
			prop.set_Value(ref propValue);
		}

		#region debug
		/*		private void WritePropertiesToFile(WIA.Properties properties)
		{
			foreach (Property prop in properties)
			{
				var name = prop.Name;
				var val = prop.get_Value();
				int? min = null;
				int? max = null;
				try
				{
					min = prop.SubTypeMin;
					max = prop.SubTypeMax;
				}
				catch (Exception)
				{
				}

				File.AppendAllText(
					"info2.txt",
					string.Format("id: {4}, name: {0}, val: {1}, min: {2}, max: {3}\r\n", name, val, min.HasValue ? min.Value.ToString() : "", max.HasValue ? max.Value.ToString() : "", prop.PropertyID));
				
			}
		}*/
		#endregion
	}
}