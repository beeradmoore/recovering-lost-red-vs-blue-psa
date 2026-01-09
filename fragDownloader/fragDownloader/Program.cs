using System.Text;
using System.Xml;

var baseUrl = "http://videos.halowaypoint.com/videos1/db6723e6038f41a1819e8e71be7776a0/";
var manifestName = "2060770-3";
var ismcFile = $"{manifestName}.ismc";
var ismFile = $"{manifestName}.ism";
var httpClient = new HttpClient();

if (File.Exists(ismcFile) == false)
{
	using (var fileStream = File.Create(ismcFile))
	{
		using (var webStream = await httpClient.GetStreamAsync($"{baseUrl}{ismcFile}"))
		{
			await webStream.CopyToAsync(fileStream);
		}
	}
}

if (File.Exists(ismFile) == false)
{
	using (var fileStream = File.Create(ismFile))
	{
		using (var webStream = await httpClient.GetStreamAsync($"{baseUrl}{ismFile}"))
		{
			await webStream.CopyToAsync(fileStream);
		}
	}
}



var doc = new XmlDocument();
doc.LoadXml(File.ReadAllText(ismcFile));

if (doc.DocumentElement is not XmlNode rootNode)
{
	Console.WriteLine("ERROR: Could not load document.");
	return;
}

var videoStreamIndexNode = rootNode.SelectSingleNode("//StreamIndex[@Type='video']");
var audioStreamIndexNode = rootNode.SelectSingleNode("//StreamIndex[@Type='audio']");

if (videoStreamIndexNode is null)
{
	Console.WriteLine("ERROR: Could not find video stream.");
	return;
}

if (audioStreamIndexNode is null)
{
	Console.WriteLine("ERROR: Could not find audio stream.");
	return;
}


var highestVideoBitrate = GetHighestBitrate(videoStreamIndexNode);
var highestAudioBitrate = GetHighestBitrate(audioStreamIndexNode);

if (highestVideoBitrate < 0 || highestAudioBitrate < 0)
{
    return;
}


// Build fragment lists
var videoFragmentPositions = GetFragmentPositions(videoStreamIndexNode);
var audioFragmentPositions = GetFragmentPositions(audioStreamIndexNode);

if (videoFragmentPositions is null || audioFragmentPositions is null)
{
	return;
}


// Download the fragments
var didDownloadVideoSegments = await DownloadFragmentsAsync("video", highestVideoBitrate, videoFragmentPositions);
var didDownloadAudioSegments = await DownloadFragmentsAsync("audio", highestAudioBitrate, audioFragmentPositions);


async Task<bool> DownloadFragmentsAsync(string downloadType, long bitrate, List<long> fragmentPositions)
{
	if (Directory.Exists(downloadType) == false)
	{
		Directory.CreateDirectory(downloadType); 
	}

	var extension = downloadType switch
	{
		"video" => "ismv",
		"audio" => "isma",
		_ => string.Empty
	};

	if (downloadType != "video" && downloadType != "audio")
	{
		Console.WriteLine("Invalid download type.");
		return false;
	}

	var fileListFile = Path.Combine(downloadType, "filelist.txt");

	if (File.Exists(fileListFile))
	{
		File.Delete(fileListFile);
	}

	using var fileListStreamWriter = File.AppendText(fileListFile);

	var appendedFile = Path.Combine(downloadType, $"{downloadType}_appended");
	using var totalFile = File.Create(appendedFile);

	var fileNameList = new List<string>();
 
	for (var i = 0; i < fragmentPositions.Count; ++i)
	{
		var faragmentUrl = $"{baseUrl}{ismFile}/QualityLevels({bitrate})/Fragments({downloadType}={fragmentPositions[i]})";
		var targetFragmentFile = $"fragment_{i}.{extension}";
		var targetFragmentPath = Path.Combine(downloadType, targetFragmentFile);

		fileNameList.Add(targetFragmentFile);
		fileListStreamWriter.WriteLine($"file '{targetFragmentFile}'");

		// Skip if it exists.
		// We are not testing if its valid or not.
		if (File.Exists(targetFragmentPath))
		{
			using (var fileStream = File.OpenRead(targetFragmentPath))
			{
				await fileStream.CopyToAsync(totalFile);
			}
			
			continue;
		}

		Console.WriteLine($"Downloading {faragmentUrl}");

		using (var fileStream = File.Create(targetFragmentPath))
		{
			using (var webStream = await httpClient.GetStreamAsync(faragmentUrl))
			{
				await webStream.CopyToAsync(fileStream);
			}

			fileStream.Position = 0;
			await fileStream.CopyToAsync(totalFile);
		}
	}

	// Trying to use filelist.txt also does not work to join them.
	// ffmpeg -f concat -safe 0 -i filelist.txt -c copy output.mp4  

	// This does not work to join them.
	// mp4box -cat fragment_0.ismv -cat fragment_1.ismv -cat fragment_2.ismv output.mp4
	var stringBuilder = new StringBuilder();
	stringBuilder.Append("mp4box");
	foreach (var fileName in fileNameList)
	{
		stringBuilder.Append($" -cat {fileName}"); 
	}
	stringBuilder.Append(" -out output.mp4");
	Console.WriteLine(stringBuilder.ToString());

	return true;
}





long GetHighestBitrate(XmlNode streamIndexNode)
{
	// Find highest quality. We are cheating and not caring about resolution because we know in this case it is the same.
	var highestBitrate = -1L;
	var qualityNodes = streamIndexNode.SelectNodes("QualityLevel");

	if (qualityNodes is null)
	{
		Console.WriteLine("ERROR: Could not find video quality nodes.");
		return -1L;
	}

	foreach (XmlNode qualityNode in qualityNodes)
	{
		if (qualityNode.Attributes?.GetNamedItem("Bitrate") is XmlAttribute bitrateAttribute)
		{
			if (long.TryParse(bitrateAttribute.Value, out var bitrateLong))
			{
				if (bitrateLong > highestBitrate)
				{
					highestBitrate = bitrateLong;
				}
			}
		}
	}

	return highestBitrate;
}

List<long>? GetFragmentPositions(XmlNode streamIndexNode)
{
	var timingNodes = streamIndexNode.SelectNodes("c");
    if (timingNodes is null)
    {
        Console.WriteLine("ERROR: Could not fetch timing nodes.");
        return null;
    }

	var fragmentPositions = new List<long>(timingNodes.Count);
    var currentDuration = 0L;

	foreach (XmlNode timingNode in timingNodes)
	{
		if (timingNode.Attributes?.GetNamedItem("d") is XmlAttribute durationAttribute)
		{
			if (long.TryParse(durationAttribute.Value, out var duration))
			{
                fragmentPositions.Add(currentDuration);
                currentDuration += duration;
			}
		}
	}

	return fragmentPositions;
}