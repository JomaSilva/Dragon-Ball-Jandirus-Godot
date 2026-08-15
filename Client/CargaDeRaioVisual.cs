using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A CARGA DO RAIO -- o brilho que nasce na mao de quem esta reunindo energia pra um feixe.
///
/// ============================ O QUE ELE E NO ORIGINAL ============================
/// `addchargeoverlay()` (`Skills/Ki/tools/mobhandler.dm:18-32`), chamado pelo bloco de carga de todo
/// verb de raio (`beams.dm:300` e as irmas). Sao cinco linhas e cada uma virou uma decisao aqui:
///
///   `I.icon = 'BlastCharges.dmi'`            -> <see cref="Arte"/>
///   `I.icon_state = ChargeState`             -> <see cref="Estado"/>, de 1 a 9, ver `EntityState.CargaDoCanal`
///   `I.icon += rgb(blastR,blastG,blastB)`    -> <see cref="Cor"/>, somada pelo shader de Ki
///   `I.layer = MOB_LAYER+99` / `I.plane = 7` -> <see cref="ZIndex"/> 1, por cima do boneco
///   `while(charging) sleep(1)`               -> quem cria e destroi e o `World`, pelo estado do fio
/// ==============================================================================
///
/// ============================ ELE NAO E A CHAMA DO `C`, E A DIFERENCA IMPORTA ============================
/// A <see cref="CargaVisual"/> ali do lado desenha a aura de POWER-UP -- a do `Power_Up` do DM, a que
/// o jogador acende segurando C, a que ilumina e zumbe. Esta aqui e outra coisa inteira: e o brilho
/// que se junta na mao ANTES de um raio sair, ele e por personagem (nove desenhos diferentes,
/// sorteados no nascimento) e ele morre no instante em que o feixe nasce.
///
/// Os nomes sao parecidos porque o DM tambem os tem parecidos (`chargeaura` x `BlastCharges`), e por
/// isso vale dizer com todas as letras: **carregar Ki e carregar um raio sao dois estados
/// independentes**, e um corpo pode estar nos dois ao mesmo tempo. Se um dia so um dos dois aparecer
/// na tela, o defeito esta em quem os LIGA (o `World`), e nao aqui.
/// ======================================================================================================
///
/// ============================ E O CORPO POR BAIXO FICA NO IDLE ============================
/// Nao ha pose de carregar, e isso nao e falta de arte -- e o original. O bloco de carga do verb
/// escreve `forceicon`, som, `canmove = 0`, `charging = 1` e este overlay, e **nao toca em
/// `icon_state`** (`beams.dm:288-300`); o corpo so muda de pose no `beaming` (`:280`). Ha ate a prova
/// de que alguem tentou o contrario e desistiu: no Boom Wave a linha esta la, comentada --
/// `//usr.icon_state="Blast"` (`beams.dm:485`).
///
/// Por isso este node desenha por CIMA de um boneco parado, em vez de trocar a folha dele.
/// =====================================================================================
///
/// QUEM O CRIA E O DESTROI e o <see cref="World.MarcarCargaDeRaio"/>, pelo `EntityState.Canal` do
/// snapshot. Ele nao guarda estado nenhum -- existir JA E o estado, o mesmo desenho da
/// <see cref="NaveDesenhada"/> (e pelo mesmo motivo: filho morre com o pai, entao o corpo que sai de
/// vista leva o brilho junto sem ninguem lembrar de apagar nada).
/// </summary>
public partial class CargaDeRaioVisual : Node2D
{
	/// <summary>O nome do node. Uma casa so: quem cria e quem destroi procuram por ele.</summary>
	public const string NomeDoNode = "CargaDeRaio";

	/// <summary>`I.icon = 'BlastCharges.dmi'` (`mobhandler.dm:5`).</summary>
	private const string Arte = "res://Assets/Sprites/Blasts/BlastCharges.tres";

	/// <summary>O mesmo shader do tiro: ele SOMA a cor por cima de uma folha cinza.</summary>
	private const string CaminhoDoShaderDeKi = "res://Assets/Shaders/Ki.gdshader";

	/// <summary>
	/// QUAL DOS NOVE DESENHOS -- o `ChargeState` do personagem. De 1 a 9.
	///
	/// Vem do fio (`EntityState.CargaDoCanal`) e nao e sorteado aqui: o desenho tem que ser o MESMO
	/// na tela de todo mundo, e um sorteio local daria a cada espectador uma carga diferente pro
	/// mesmo corpo. Quem o deriva e o servidor, por funcao pura da identidade -- ver
	/// `ArteDeProjetil.CargaDeRaio`.
	/// </summary>
	public int Estado = 1;

	/// <summary>
	/// `I.icon += rgb(blastR,blastG,blastB)` (`mobhandler.dm:8`) -- a cor de Ki do DONO, e nao a da
	/// chama. Ver `World.CorDoKiDe`, que e quem a resolve e por que as duas nao sao a mesma pergunta.
	/// </summary>
	public Color Cor = Aura.CorDoKiCru;

	/// <summary>
	/// PRA ONDE O CORPO OLHA. A folha e direcional (`1_north` .. `9_west`) porque no BYOND o overlay
	/// e filho do mob e HERDA o `dir` dele -- o brilho nasce na MAO, e a mao muda de lado.
	///
	/// O `8` e a excecao e ela e da propria folha: aquele estado nao tem sufixo de direcao nenhum. Um
	/// personagem com carga 8 desenha o mesmo brilho pros quatro lados, e isso e o original.
	/// </summary>
	public Facing Direcao = Facing.South;

	private AnimatedSprite2D? _sprite;
	private SpriteFrames? _folha;

	public override void _Ready()
	{
		// POR CIMA DO BONECO -- `layer = MOB_LAYER+99`, `plane = 7` (`mobhandler.dm:3-4`). O brilho
		// que se junta na mao fica na FRENTE do corpo; desenhado por tras ele viraria um halo atras
		// dos ombros, que e outra coisa.
		ZIndex = 1;

		if (!ResourceLoader.Exists(Arte)) { GD.PushWarning($"[carga de raio] sem arte em {Arte}"); return; }
		_folha = ResourceLoader.Load<SpriteFrames>(Arte);
		if (_folha == null) return;

		_sprite = new AnimatedSprite2D
		{
			SpriteFrames = _folha,
			Centered = true,
			// O MESMO 4 PRA BAIXO DA NAVE, e pelo mesmo motivo: o centro do node do corpo fica
			// `MoveRules.FeetOffsetY` acima dos pes, e sem o ajuste o brilho sai na altura da testa.
			Position = new Vector2(0, 4),
		};

		if (ResourceLoader.Load<Shader>(CaminhoDoShaderDeKi) is { } sh)
		{
			var m = new ShaderMaterial { Shader = sh };
			m.SetShaderParameter("tinta", new Vector3(Cor.R, Cor.G, Cor.B));
			_sprite.Material = m;
		}

		AddChild(_sprite);
		Aplicar();

		// ============================ A CARGA TAMBEM LANCA LUZ, E AQUI ESTA O PORQUE ============================
		// O pedido do dono foi *"beams e ataque de ki deveriam ter LUZ PROPRIA"*, e o brilho que se
		// junta na mao E um ataque de ki -- e literalmente a arte do raio (`BlastCharges.dmi`),
		// tingida com a cor de ki do mesmo dono, e ele morre no instante em que o feixe nasce. Um
		// brilho de energia que nao acende nada em volta le como adesivo colado no boneco.
		//
		// ============================ E ELA NAO E A CHAMA DO `C`, QUE CONTINUA SEM LUZ ============================
		// A <see cref="CargaVisual"/> ali do lado -- a aura de power-up que o jogador acende segurando
		// C -- continua sem `PointLight2D` nenhuma, por decisao escrita do dono (ver `Aura.Aplicar`:
		// dar luz a ela seria um TERCEIRO dono do mesmo efeito, e faria a BASE voltar a brilhar, que e
		// a queixa que aquele conserto existe pra matar). Carregar KI e carregar UM RAIO sao dois
		// estados independentes, e este arquivo ja dizia isso no cabecalho. So o segundo acende.
		//
		// CUSTA POUCO por construcao: e no maximo UMA luz por corpo canalizando, e canalizar e um
		// estado raro e curto -- contra as ate 256 de um ceu cheio de tiro, que e o que obrigou o
		// teto do `LuzDeKi`. As duas dividem o mesmo orcamento, e e de proposito: o que estoura um
		// quadro e a soma, e nao a origem de cada uma.
		// ====================================================================================================
		LuzDeKi.Pendurar(this, Cor, 1f);
	}

	/// <summary>
	/// O CORPO VIROU (ou o estado mudou). Chamado pelo `World` a cada snapshot enquanto a carga vive.
	///
	/// SO TRABALHA NA MUDANCA -- o `Animation` do `AnimatedSprite2D` ja e o "mudou?" e reescrever o
	/// mesmo nome 30 vezes por segundo reinicia a animacao, que na pratica CONGELA o brilho no
	/// primeiro quadro. E o mesmo cuidado que o `SetState` do `CharacterVisual` toma.
	/// </summary>
	public void Definir(int estado, Facing dir)
	{
		if (estado == Estado && dir == Direcao) return;
		Estado = estado;
		Direcao = dir;
		Aplicar();
	}

	private void Aplicar()
	{
		if (_sprite == null || _folha == null) return;

		int n = Estado is >= 1 and <= 9 ? Estado : 1;

		// A ESCADA E CURTA E TEM SO UM DEGRAU, e o degrau e o `8`: ele e o unico estado da folha sem
		// sufixo de direcao. Tentar `<n>_<dir>` primeiro e cair no `<n>` cru cobre os dois casos sem
		// um `if (n == 8)` escrito -- que envelheceria no dia em que a folha mudasse.
		string dir = MoveRules.FacingSuffix(Direcao);
		string anim = _folha.HasAnimation($"{n}_{dir}") ? $"{n}_{dir}"
					: _folha.HasAnimation($"{n}") ? $"{n}"
					: "";
		if (anim.Length == 0) return;

		_sprite.Animation = anim;
		_sprite.Play();
	}
}
