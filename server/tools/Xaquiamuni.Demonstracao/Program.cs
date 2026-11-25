using Xaquiamuni.Tests.Integration;

// WebApplicationFactory resolves its generated content-root manifest from the working directory.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
return await DemonstracaoLocal.ExecutarAsync(args);
