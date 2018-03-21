using FMOD;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityRipper;
using UnityRipper.AssetExporters;
using UnityRipper.Classes;
using UnityRipper.Classes.AudioClips;

using Object = UnityRipper.Classes.Object;

namespace UnityRipperFull.Exporters
{
	public class AudioAssetExporter : AssetExporter
	{
		public override IExportCollection CreateCollection(Object @object)
		{
			return new AssetExportCollection(this, @object);
		}

		public override bool Export(IAssetsExporter exporter, IExportCollection collection, string dirPath)
		{
			AssetExportCollection asset = (AssetExportCollection)collection;
			AudioClip audioClip = (AudioClip)asset.Asset;
			exporter.File = audioClip.File;
			
			string subFolder = audioClip.ClassID.ToString();
			string subPath = Path.Combine(dirPath, subFolder);
			string fileName = GetUniqueFileName(audioClip, subPath);
			if (IsSupported(audioClip))
			{
				fileName = $"{Path.GetFileNameWithoutExtension(fileName)}.wav";
			}
			string filePath = Path.Combine(subPath, fileName);

			if(!Directory.Exists(subPath))
			{
				Directory.CreateDirectory(subPath);
			}

			exporter.File = audioClip.File;

			using (FileStream fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write))
			{
				if (IsSupported(audioClip))
				{
					ExportAudioClip(fileStream, audioClip);
				}
				else
				{
					Logger.Instance.Log(LogType.Warning, LogCategory.Export, $"AudioClip type {GetAudioType(audioClip)} isn't supported");
					audioClip.ExportBinary(exporter, fileStream);
				}
			}

			ExportMeta(exporter, asset, filePath);
			return IsSupported(audioClip);
		}
		
		public override AssetType ToExportType(ClassIDType classID)
		{
			return AssetType.Meta;
		}

		private bool IsSupported(AudioClip audioClip)
		{
			if (AudioClip.IsReadType(audioClip.File.Version))
			{
				switch (audioClip.Type)
				{
					case FMODSoundType.ACC:
					case FMODSoundType.AIFF:
					case FMODSoundType.IT:
					case FMODSoundType.MOD:
					case FMODSoundType.MPEG:
					case FMODSoundType.OGGVORBIS:
					case FMODSoundType.S3M:
					case FMODSoundType.WAV:
					case FMODSoundType.XM:
					case FMODSoundType.XMA:
					case FMODSoundType.VAG:
					case FMODSoundType.AUDIOQUEUE:
						return true;
					default:
						return false;
				}
			}
			else
			{
				switch (audioClip.CompressionFormat)
				{
					case AudioCompressionFormat.PCM:
					case AudioCompressionFormat.ADPCM:
					case AudioCompressionFormat.Vorbis:
					case AudioCompressionFormat.MP3:
					case AudioCompressionFormat.GCADPCM:
					case AudioCompressionFormat.VAG:
					case AudioCompressionFormat.HEVAG:
					case AudioCompressionFormat.XMA:
					case AudioCompressionFormat.AAC:
					case AudioCompressionFormat.ATRAC9:
						return true;
					default:
						return false;
				}
			}
		}

		private string GetAudioType(AudioClip audioClip)
		{
			if (AudioClip.IsReadType(audioClip.File.Version))
			{
				return audioClip.Type.ToString();
			}
			else
			{
				return audioClip.CompressionFormat.ToString();
			}
		}
		
		private byte[] GetRawData(AudioClip clip)
		{
			if (AudioClip.IsReadLoadType(clip.File.Version))
			{
				ResourcesFile res = clip.File.Collection.FindResourcesFile(clip.File, clip.FSBResource.Source);
				if (res == null)
				{
					Logger.Instance.Log(LogType.Warning, LogCategory.Export, $"Can't export '{clip.Name}' because resources file '{clip.FSBResource.Source}' wasn't found");
					return null;
				}

				res.Stream.Position = clip.FSBResource.Offset;
				if (StreamedResource.IsReadSize(clip.File.Version))
				{
					byte[] buffer = new	byte[clip.FSBResource.Size];
					res.Stream.Read(buffer, 0, buffer.Length);
					return buffer;
				}
				else
				{
					Logger.Instance.Log(LogType.Warning, LogCategory.Export, $"Can't export '{clip.Name}' because unknown raw data size");
					return null;
				}
			}
			else
			{
				return (byte[])clip.AudioData;
			}
		}

		private void ExportAudioClip(FileStream fileStream, AudioClip clip)
		{
			CREATESOUNDEXINFO exinfo = new CREATESOUNDEXINFO();
			FMOD.System system = null;
			Sound sound = null;
			Sound subsound = null;

			try
			{
				RESULT result = Factory.System_Create(out system);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't create factory for AudioClip {clip.Name}");
					return;
				}

				result = system.init(1, INITFLAGS.NORMAL, IntPtr.Zero);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't init system for AudioClip {clip.Name}");
					return;
				}

				byte[] data = GetRawData(clip);
				if (data == null)
				{
					return;
				}
			
				exinfo.cbsize = Marshal.SizeOf(exinfo);
				exinfo.length = (uint)data.Length;
				result = system.createSound(data, MODE.OPENMEMORY, ref exinfo, out sound);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't create sound for AudioClip {clip.Name}");
					return;
				}

				result = sound.getSubSound(0, out subsound);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't get subsound for AudioClip {clip.Name}");
					return;
				}

				result = subsound.getFormat(out SOUND_TYPE type, out SOUND_FORMAT format, out int numChannels, out int bitsPerSample);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't get format for AudioClip {clip.Name}");
					return;
				}

				result = subsound.getDefaults(out float frequency, out int priority);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't get defaults for AudioClip {clip.Name}");
					return;
				}

				int sampleRate = (int)frequency;
				result = subsound.getLength(out uint length, TIMEUNIT.PCMBYTES);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't get length for AudioClip {clip.Name}");
					return;
				}

				result = subsound.@lock(0, length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't lock for AudioClip {clip.Name}");
					return;
				}

				using (BinaryWriter writer = new BinaryWriter(fileStream))
				{
					writer.Write(Encoding.UTF8.GetBytes("RIFF"));
					writer.Write(len1 + 36);
					writer.Write(Encoding.UTF8.GetBytes("WAVEfmt "));
					writer.Write(16);
					writer.Write((short)1);
					writer.Write((short)numChannels);
					writer.Write(sampleRate);
					writer.Write(sampleRate * numChannels * bitsPerSample / 8);
					writer.Write((short)(numChannels * bitsPerSample / 8));
					writer.Write((short)bitsPerSample);
					writer.Write(Encoding.UTF8.GetBytes("data"));
					writer.Write(len1);

					for (int i = 0; i < len1; i++)
					{
						byte value = Marshal.ReadByte(ptr1, i);
						writer.Write(value);
					}
				}

				result = subsound.unlock(ptr1, ptr2, len1, len2);
				if (result != RESULT.OK)
				{
					Logger.Instance.Log(LogType.Error, LogCategory.Export, $"Can't unlock for AudioClip {clip.Name}");
				}
			}
			finally
			{
				if (subsound != null)
				{
					subsound.release();
				}
				if (sound != null)
				{
					sound.release();
				}
				if (system != null)
				{
					system.release();
				}
			}
		}
	}
}
