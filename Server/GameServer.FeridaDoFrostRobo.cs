using Jandirus.Core.Combat;

namespace Jandirus.Server;

/// <summary>
/// O GANCHO DO ROBO `--diagferidafrost` (2026-10-19) -- ver `Client/RoboDaFeridaDoFrost.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`). O corpo vira Frost Demon pelo gancho do `--diagcorpofrost` (`VirarFrostDeTeste`); aqui ele apanha.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A VIDA DE CADA MEMBRO NA FOTO: 30% -- dano de 70%, o roxo cheio (`Feridas.HematomaCheio`, 50%) e o sangue no meio do caminho
	/// (55% a 90%). Acima dos 20% em que o nucleo quebra e nocauteia (`Regras.LimiarQuebra`): o corpo apanha e continua de pe.
	/// </summary>
	private const double VidaNaFotoDaFerida = 0.30;

	/// <summary>
	/// O corpo do robo apanha pra foto: todo membro de fora (o interno nao se mira nem se desenha) com <see cref="VidaNaFotoDaFerida"/>, e o
	/// BRACO DIREITO decepado pelo funil do corpo (`Body.Decepar`, que leva a mao junto). Pode ser chamado de novo: a mascara sai igual.
	///
	/// E A TAG DE COMBATE ACESA, de proposito: com ela no ar a cura passiva do Frost Demon para (`Regeneracao.PodeCurar` -- Reg 1, sem
	/// `fastRegen`), e a mascara nao muda sozinha no meio do roteiro. Cada cena do robo mede a ferida DESTA linha, e nao uma costura que
	/// chegou no caminho e escreveu a camada por outra porta.
	/// </summary>
	internal bool FerirParaFotoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo is not { } corpo) return false;
		foreach (BodyPart p in corpo.Partes)
			if (!p.Decepado && p.Papel != Vitalidade.Interno) p.Vida = p.VidaMax * VidaNaFotoDaFerida;
		if (corpo.Achar("Braco direito") is not { } braco) return false;
		corpo.Decepar(braco);
		corpo.Vida();
		pl.Combate.SincronizarVida();
		pl.Combate.EntrarEmCombate(comAlguem: false);
		return true;
	}

	/// <summary>
	/// Mais um membro decepado pelo mesmo funil (`Body.Decepar`), com a tag de combate de novo -- a cena do pedaco solto
	/// (2026-10-19): o braco ESQUERDO do "Changeling 1 Large", que de frente cai no lado da imagem por onde sobe o rabo.
	/// </summary>
	internal bool DeceparParaFotoDeTeste(int id, string membro)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo is not { } corpo
			|| corpo.Achar(membro) is not { } parte) return false;
		corpo.Decepar(parte);
		corpo.Vida();
		pl.Combate.SincronizarVida();
		pl.Combate.EntrarEmCombate(comAlguem: false);
		return true;
	}
}
