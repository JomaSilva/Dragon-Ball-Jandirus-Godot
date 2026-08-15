using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS JANELAS DA BANCADA QUE FOTOGRAFA A POSE DO CORPO COM O RAIO NA MAO
/// (`--diagpose`, `Client/RoboDeFotoDaPose.cs`).
///
/// ============================ POR QUE ELA EXISTE, SE A `--projetilteste` 4b JA E VERDE ============================
/// A familia 4b mede a pose no ESTADO DO FIO (`EstadoDe(...).Pose`) e mede bem: a entrada, as tres
/// saidas, o soco que atravessa e devolve, a trava de direcao, o NPC e o byte opcional. Nada disso e
/// pouco -- e nada disso e um pixel.
///
/// Entre `Pose.Canalizando` e o boneco na tela ha um caminho inteiro que aquela bancada pula:
///
///     `EntityState.Write` -> `Read` -> `World` (laco de snapshot) -> `RemotePlayer.Receive` **ou**
///     `LocalPlayer.ReceberCanalDeKi` -> `PorAPoseDoCorpo` -> `CharacterVisual.SetPose` ->
///     `Escolher` (a escada `blast` -> `attack` -> ...) -> a folha do personagem
///
/// Um elo qualquer daquela fila escrito e nao ligado devolve exatamente a queixa do dono -- *"vc n
/// colocou a ANIMACAO NO SPRITE dos personagem ao SOLTAR O BEAM"* -- com a bancada de numero
/// inteirinha verde. Este projeto ja pagou esse cego quatro vezes (ver a memoria "a bancada mede
/// INTENCAO"), e a ultima foi nesta mesma funcionalidade: **229 folhas de beam ficaram meses
/// importadas e sem um consumidor**.
/// ============================================================================================================
///
/// ============================ O QUE ESTAS JANELAS **NAO** FAZEM ============================
/// Nao escrevem `Pose`, nao escrevem `_canais` e nao chamam `Canalizar`, `SoltarCanal` nem
/// `DerrubarRaioPorGolpe`. O canal desta bancada nasce e morre pelo BOTAO
/// (<see cref="GatilhoDaVariedade"/> -> `UsarHabilidade` -> `KiWave`), o golpe sai pelo
/// <see cref="Atacar"/> de producao e o Ki acaba porque o `TickDosCanaisDeKi` cobrou o aluguel.
///
/// A unica coisa que elas escrevem e o que um JOGADOR tambem escreveria: pra onde ele olha, quanto
/// Ki ele tem, e o soco que ele deu.
/// ========================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O QUE A FOTO PRECISA LER DE CADA CORPO -- e nenhum destes campos e escrito por esta bancada.
	///
	/// `Canal`/`Atirando`/`Carga` saem do <see cref="CanalDeKiDe"/>, que e a MESMA leitura que o
	/// `EstadoDe` publica no snapshot. Ler daqui e ler o que a tela recebeu.
	///
	/// O RESTO E O CONTRA-EXEMPLO. Uma foto de corpo no idle nao diz POR QUE ele esta no idle: caiu
	/// de nocaute, morreu, esta voando por um arremesso? Sem estes quatro, "a pose saiu" e "o corpo
	/// desmaiou" dariam a mesma imagem -- e a primeira rodada da `--diagraio` reprovou exatamente
	/// nesse buraco (ver `CenaDeFoto`).
	/// </summary>
	internal (bool Canal, bool Atirando, int Carga, bool KO, bool Morto, int TiquesDeVoo,
			  double Ki, double MaxKi, double Vida, Facing Olhar) EstadoDaPose(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl))
			return (false, false, 0, true, true, 0, 0, 0, 0, Facing.South);

		(bool canal, bool atirando, int carga) = CanalDeKiDe(id);
		return (canal, atirando, carga, pl.Ficha.KO, pl.Ficha.dead, pl.TiquesDeVoo,
				pl.Ficha.Ki, pl.Ficha.MaxKi, pl.Ficha.HP, pl.Facing);
	}

	/// <summary>
	/// TODOS OS RUMOS EM QUE O RAIO TEM ESPACO PRA ANDAR -- a irma plural da
	/// <see cref="RumoLivreDaVariedade"/>, e ela existe por causa de UM pedido do dono.
	///
	/// *"atire pra dois lados e mostre que a pose vira junto"*: com um rumo so, a cena da direcao nao
	/// tem como acontecer. E os dois rumos precisam ser LIVRES pelo mesmo motivo que o primeiro --
	/// um raio que morre numa pedra a dois tiles fecha o canal antes do obturador, e a foto sairia
	/// com o corpo ja no idle por um motivo que nao e o que se esta medindo.
	///
	/// A ordem e a mesma da irma (Sul, Leste, Norte, Oeste), entao o primeiro elemento desta lista e
	/// exatamente o que aquela devolveria -- as duas nunca discordam.
	/// </summary>
	internal Facing[] RumosLivresDaPose(int id, int tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return [];
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return [];

		const int T = ZoneCollision.TileSize;
		int cx = (int)Math.Floor(pl.Pos.X / T), cy = (int)Math.Floor(pl.Pos.Y / T);

		var livres = new List<Facing>();
		foreach (Facing f in (Facing[])[Facing.South, Facing.East, Facing.North, Facing.West])
		{
			Vec2 d = MeleeArea.Frente(f);
			bool livre = true;
			for (int k = 1; k <= tiles && livre; k++)
				if (mapa.BlockedCell(cx + (int)d.X * k, cy + (int)d.Y * k)) livre = false;
			if (livre) livres.Add(f);
		}
		return [.. livres];
	}

	/// <summary>
	/// O TANQUE VAZIO -- o gatilho da SAIDA 2 (*"sua energia acaba e o raio morre na mao"*).
	///
	/// E o espelho exato do <see cref="RegarOKiDeFoto"/>, e e tao legitimo quanto ele: o que se
	/// escreve aqui e a ENTRADA da regra (quanto Ki ha), nunca a regra. Quem decide que o canal cai
	/// continua sendo o `TickDosCanaisDeKi`, no ciclo de 0,2 s, e quem apaga a pose continua sendo o
	/// `FecharCanal`.
	///
	/// Esperar o raio drenar sozinho tambem funcionaria -- e demoraria minutos com o tanque que a
	/// bancada precisa ter pra chegar ate aqui. A familia 4b da `--projetilteste` faz exatamente
	/// isto, com a mesma linha.
	/// </summary>
	internal void SecarOKiDaPose(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.Ki = 0;
	}

	/// <summary>
	/// UM CORPO QUE BATE FORTE E MACHUCA POUCO -- e as duas metades sao necessarias.
	///
	/// ============================ POR QUE O SOCO PRECISA SER CALIBRADO ============================
	/// A saida 3 e *"ALGUEM BATEU NELE e cancelou o beam"*, e ela so acontece pelo ARREMESSO: e o
	/// `Arremessar` que chama o `DerrubarRaioPorGolpe` (o `if(KB) stopbeaming()` de `beams.dm:73-74`).
	/// Pra haver arremesso, `ForcaDoPesado = (dmg + Ephysoff*2 + 1) * check` tem que passar do
	/// `Limiar` -- entao o atacante precisa de MUITO `physoff`.
	///
	/// Mas a foto de DEPOIS tem que mostrar um corpo **em pe, no idle**. Se o mesmo soco nocautear, a
	/// pose vira `ko` e a bancada mediria a saida errada -- o `TickDosCanaisDeKi` derruba o canal por
	/// nocaute (`AreYaBeamingKid`) muito antes de o arremesso encostar. Foto verde, regra nao provada.
	///
	/// Dai o desenho: `physoff` alto (que entra na FORCA em dobro) e BP baixo em relacao ao alvo (que
	/// e o que segura o DANO, via `BpModulus`). Um bracinho de ferro num corpo fraco.
	/// ========================================================================================
	///
	/// `Knockback = true` ja e o valor de fabrica (`GameServer.cs:543`), e esta escrito assim mesmo
	/// porque e a UNICA pre-condicao que o `TentarEmpurrar` confere antes de tudo
	/// (`if (!a.Knockback ...) return`): no dia em que alguem mudar aquele padrao -- ou em que uma
	/// tecnica desligar o empurrao do corpo, como o lote G3 ja faz --, esta bancada mediria "o soco
	/// nao cancela o raio" sobre um atacante que simplesmente nao arremessa ninguem.
	/// </summary>
	internal void ArmarOSocoDaPose(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;

		pl.Ficha.physoff = Math.Max(pl.Ficha.physoff, 400);
		pl.Ficha.technique = Math.Max(pl.Ficha.technique, 400);
		pl.Ficha.speed = Math.Max(pl.Ficha.speed, 400);
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Ficha.stamina = pl.Ficha.maxstamina;
		pl.Knockback = true;
	}

	/// <summary>
	/// O SOCO, PELA PORTA DE PRODUCAO -- <see cref="Atacar"/>, a mesma funcao que o
	/// `case Protocol.C2S.Golpe` chama quando um jogador aperta espaco.
	///
	/// O ALVO E MARCADO ANTES porque e o que faz o corpo VIRAR e o `Aproximar` fechar a distancia --
	/// exatamente o que o duplo clique do jogador faz. Sem isso o soco sairia no ar pro lado em que o
	/// atacante nasceu olhando, e a bancada mediria um soco que nao encostou em ninguem.
	///
	/// DEVOLVE O QUE MUDOU NO ALVO, e nao "deu certo": um soco pode ser esquivado, aparado ou nao
	/// arremessar (o `Avaliar` tem quatro desfechos). Quem chama repete ate o canal cair ou o prazo
	/// vencer -- e o numero de socos entra no relatorio, porque um raio que so cai no vigesimo soco e
	/// uma resposta diferente de um que cai no primeiro.
	/// </summary>
	internal (bool Canal, bool KO, int TiquesDeVoo) SocarNaPose(int atacante, int alvo)
	{
		if (!_players.TryGetValue(atacante, out ServerPlayer? a)
			|| !_players.TryGetValue(alvo, out ServerPlayer? d))
			return (false, true, 0);

		a.AlvoId = alvo;
		Atacar(a, Protocol.Golpe.Pesado);
		return (_canais.ContainsKey(alvo), d.Ficha.KO, d.TiquesDeVoo);
	}

	/// <summary>
	/// A RECARGA DO SOCO ZERADA -- e so ela.
	///
	/// `Atacar` sai na primeira linha se `PodeAtacar()` disser nao, e uma das razoes de nao e a
	/// cadencia (`ca.Recarga`), que num corpo de 400 de velocidade ainda e um terco de segundo. A
	/// bancada quer socar em quadros seguidos ate o arremesso sair; sem isto, dezenove das vinte
	/// tentativas seriam recusadas caladas e o relatorio diria "vinte socos" tendo dado um.
	///
	/// NAO MEXE em atordoamento, nocaute nem morte -- as outras tres recusas do `PodeAtacar` --,
	/// porque essas TEM que continuar valendo: um atacante nocauteado que continuasse socando seria
	/// a bancada inventando um golpe que o jogo nao permite.
	/// </summary>
	internal void ProntoParaSocarDeNovo(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { } c) c.Recarga = 0;
	}
}
