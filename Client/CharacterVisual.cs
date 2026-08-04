using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O corpo visivel de um personagem: uma PILHA DE CAMADAS que consomem os SpriteFrames que o
/// Tools/AssetPipeline gerou a partir dos .dmi.
///
///     corpo  ->  roupa (ate 4)  ->  cabelo  ->  olhos
///
/// DUAS COISAS QUE PARECEM DETALHE E NAO SAO:
///
/// 1) AS CAMADAS ANDAM TRAVADAS NO MESMO QUADRO. Cada camada tem a folha completa (o .dmi de
///    roupa e de cabelo tem os mesmos estados do corpo), mas deixar cada AnimatedSprite2D
///    tocar sozinha faz elas comecarem em instantes diferentes e SAIREM DE FASE -- a camisa
///    andando fora do passo do corpo. So o CORPO toca; as outras copiam o quadro dele. E o
///    que o BYOND garantia com `VIS_INHERIT_ICON_STATE`.
///
/// 2) COR E SOMADA, NAO MULTIPLICADA. `modulate` multiplica, e os sprites JA VEM COLORIDOS:
///    multiplicar cabelo preto por preto da preto CHAPADO, sem os realces -- o cabelo vira
///    uma silhueta. O jogo usava `ICON_ADD`, que clareia sem apagar o desenho. Aqui isso e um
///    shader de 3 linhas, e cor nula = nao mexe em nada.
///
/// COMO O BYOND ANIMAVA: o estado de nome vazio (aqui "default") tem `movement = 1`, ou seja,
/// ele SO roda enquanto o personagem anda; parado, mostra o primeiro quadro. Os outros
/// estados (meditar, treinar, socar, voar) rodam sozinhos.
/// </summary>
public partial class CharacterVisual : Node2D
{
	/// <summary>
	/// O SHADER DE TODA CAMADA DO PERSONAGEM: a tinta do BYOND, os efeitos de impacto e as
	/// FERIDAS. Um arquivo `.gdshader` de verdade, e nao uma string aqui dentro.
	///
	/// ============================ POR QUE ARQUIVO, E NAO CONSTANTE ============================
	/// Ele era uma `const string` compilada junto. Funcionava -- e cobrava um preco alto pra quem
	/// AJUSTA o efeito: cada meio ponto de intensidade de sangue custava recompilar o C# e reabrir
	/// o jogo. Um efeito procedural nao se acerta lendo codigo, se acerta arrastando o valor e
	/// OLHANDO; foi assim que o dono acertou o do jogo anterior, e ele pediu o mesmo aqui.
	///
	/// Como `.gdshader`, o editor do Godot abre, mostra os uniformes num painel e a previa
	/// atualiza enquanto se arrasta -- exatamente o print que ele mandou de referencia.
	/// ==========================================================================================
	///
	/// CARREGADO UMA VEZ e compartilhado por todas as camadas de todos os personagens; cada
	/// camada tem so o seu `ShaderMaterial`, com os seus valores.
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Personagem.gdshader";

	private static Shader? _shaderTinta;

	private static Shader ShaderTinta => _shaderTinta ??= Carregar();

	private static Shader Carregar()
	{
		if (ResourceLoader.Load<Shader>(CaminhoDoShader) is { } sh) return sh;

		// SEM O ARQUIVO O PERSONAGEM NAO E DESENHADO DIREITO -- a tinta e o que da cor de pele e
		// de cabelo. Um shader vazio deixaria o boneco na cor do arquivo bruto, e sem esta linha
		// ninguem saberia por que.
		GD.PushError($"[visual] nao achei {CaminhoDoShader} -- o personagem vai sair sem tinta nem ferida");
		return new Shader { Code = "shader_type canvas_item;" };
	}

	private readonly List<AnimatedSprite2D> _camadas = [];
	private AnimatedSprite2D? _corpo;
	private AnimatedSprite2D? _cabelo, _olhos, _rabo;
	private readonly List<AnimatedSprite2D> _roupa = [];

	/// <summary>
	/// O sprite do RABO. E uma folha completa (walk/train/flight/ko nas quatro direcoes),
	/// entao ele anda travado no mesmo quadro do corpo como qualquer outra camada.
	/// </summary>
	public const string SpriteDoRabo = "res://Assets/Sprites/Clothes/Tail.tres";

	/// <summary>Este personagem tem rabo AGORA (o servidor manda; arrancar tira na hora).</summary>
	private bool _temRabo;

	private Facing _facing = Facing.South;
	private bool _moving;
	private string _state = "default";

	/// <summary>
	/// A FAMILIA DE POSE do momento. O ciclo de caminhada e um estado SEPARADO do parado --
	/// no .dmi sao dois estados de nome vazio, um marcado com `movement = 1` (a caminhada) e
	/// outro sem (a pose parada, que tem animacao propria: no corpo sao 4 quadros com o
	/// ultimo segurando 30 decimos, uma respiracao). O BYOND trocava entre os dois sozinho
	/// conforme o mob andava, e e o que se faz aqui.
	/// </summary>
	private string Familia() => _state == "default" ? (_moving ? "walk" : "default") : _state;

	public override void _Ready()
	{
		Garantir();
		Aplicar(force: true);
	}

	/// <summary>A camada do corpo tem que existir antes de qualquer coisa ser vestida.</summary>
	private void Garantir() => _corpo ??= NovaCamada(0);

	/// <summary>
	/// Uma camada nova. <paramref name="ordem"/> e a posicao dela na PILHA do personagem
	/// (corpo 0, rabo 1, roupa 2.., cabelo 10, olhos 11) -- e nao um z_index.
	///
	/// POR QUE NAO E MAIS z_index. O mundo passou a ordenar por Y, e no Godot o z_index vence
	/// a ordenacao por Y sempre: quem esta em z 10 desenha depois de TUDO que esta em z 0,
	/// esteja onde estiver. Com o cabelo em 10 e as arvores em 0, o corpo sumia atras da
	/// arvore e o cabelo continuava aparecendo por cima dela -- o personagem virava um tufo de
	/// cabelo flutuando na copa.
	///
	/// Agora todas as camadas ficam em z 0 e a pilha e a ORDEM NA ARVORE de nodes (ver
	/// <see cref="Reordenar"/>). Assim o personagem inteiro ocupa um unico degrau de z e o Y
	/// decide, que era a intencao desde o comeco.
	/// </summary>
	private AnimatedSprite2D NovaCamada(int ordem)
	{
		var s = new AnimatedSprite2D
		{
			Centered = true,
			Material = new ShaderMaterial { Shader = ShaderTinta },
		};
		s.SetMeta("ordem", ordem);

		// A CAIXA DO QUADRO TEM QUE ACOMPANHAR A ANIMACAO.
		//
		// O shader de ferida divide o corpo em faixas (cabeca em cima, pernas embaixo) e pra isso
		// precisa do UV DO QUADRO -- mas `UV` cobre a FOLHA inteira, e cada pose mora num
		// retangulo diferente dela. Sem reenviar a caixa a cada troca de quadro, as faixas ficariam
		// travadas no primeiro quadro e a ferida escorreria pelo corpo enquanto o boneco anda.
		//
		// Pelo SINAL, e nao por quadro de render: `FrameChanged` dispara quando a pose troca de
		// verdade (~5-10 Hz), e nao 60 vezes por segundo pra reescrever o mesmo valor.
		s.FrameChanged += () => AtualizarCaixa(s);
		s.AnimationChanged += () => AtualizarCaixa(s);

		AddChild(s);
		_camadas.Add(s);
		return s;
	}

	/// <summary>Manda pro shader onde este quadro comeca e acaba dentro da folha.</summary>
	private static void AtualizarCaixa(AnimatedSprite2D s)
	{
		if (!IsInstanceValid(s) || s.Material is not ShaderMaterial m) return;
		if (s.SpriteFrames is not { } sf || s.Animation.IsEmpty) return;
		if (s.Frame < 0 || s.Frame >= sf.GetFrameCount(s.Animation)) return;

		(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(sf.GetFrameTexture(s.Animation, s.Frame));
		m.SetShaderParameter("quadro_min", min);
		m.SetShaderParameter("quadro_max", max);
	}

	/// <summary>
	/// Poe as camadas na arvore na ordem da pilha.
	///
	/// Precisa ser CHAMADO, e chamado depois de toda mexida: a ordem de criacao nao e a ordem
	/// de desenho. O rabo nasce por ULTIMO (MontarRabo roda no fim de Vestir) e precisa
	/// desenhar em SEGUNDO; uma camisa vestida numa troca posterior entra no fim da arvore mas
	/// pertence ao meio da pilha. Sem isto, a ordem de desenho passaria a depender de quando o
	/// jogador trocou de roupa.
	/// </summary>
	private void Reordenar()
	{
		// a lista pode ter camada que ja saiu da arvore numa troca de aparencia
		_camadas.RemoveAll(s => !IsInstanceValid(s));
		_camadas.Sort((a, b) => a.GetMeta("ordem", 0).AsInt32() - b.GetMeta("ordem", 0).AsInt32());
		for (int i = 0; i < _camadas.Count; i++) MoveChild(_camadas[i], i);
	}

	/// <summary>
	/// Tira a camada de cena AGORA e so depois marca pra liberar.
	///
	/// `QueueFree` sozinho deixa o node na arvore ate o fim do quadro -- e o <see cref="Reordenar"/>
	/// do mesmo quadro contaria com ele nos indices, embaralhando as camadas vivas.
	/// </summary>
	private void Descartar(AnimatedSprite2D s)
	{
		_camadas.Remove(s);
		RemoveChild(s);
		s.QueueFree();
	}

	/// <summary>
	/// PINTA O CABELO POR CIMA da cor da ficha -- e a transformacao que chama.
	///
	/// Guarda a cor natural na primeira vez: passar `null` devolve o personagem ao que ele era,
	/// e sem esse registro o Saiyajin voltaria da forma com o cabelo dourado pra sempre.
	///
	/// A tinta e SOMADA (o `ICON_ADD` do original), entao dourar um cabelo preto de verdade
	/// clareia sem apagar o desenho dos fios -- que e o motivo de o shader existir.
	/// </summary>
	public void PintarCabelo(Color? cor)
	{
		if (_cabelo == null) return;
		if (_cabelo.Material is not ShaderMaterial m) return;

		if (!_corNaturalGuardada)
		{
			_corNaturalGuardada = true;
			_corNatural = m.GetShaderParameter("tinta").AsVector3();
		}

		m.SetShaderParameter("tinta", cor is { } c
			? new Vector3(c.R, c.G, c.B)
			: _corNatural);
	}

	private bool _corNaturalGuardada;
	private Vector3 _corNatural;

	// =====================================================================
	// FERIDAS
	// =====================================================================
	/// <summary>Quem recebe ferida, e de que tipo. Ver o bloco `ferida_modo` no shader.</summary>
	private const int ModoNada = 0, ModoPele = 1, ModoPano = 2;

	/// <summary>A ultima mascara aplicada -- pra nao reescrever dez uniformes por quadro.</summary>
	private Jandirus.Core.Combat.MascaraDeFeridas _feridas;
	private bool _temFeridas;

	/// <summary>
	/// O SORTEIO DESTE CORPO. Dois lutadores com o mesmo estrago tem manchas em lugares
	/// diferentes -- sem isto, uma briga de dois deixaria os dois com o MESMO respingo, e a
	/// coincidencia denuncia o efeito como desenho gerado.
	/// </summary>
	private float _semente;

	/// <summary>
	/// PINTA (ou rasga) o corpo conforme o estrago que o servidor mandou.
	///
	/// ============================ QUEM RECEBE O QUE ============================
	///   * CORPO -> hematoma e sangue. E a pele: ela fica roxa e depois sangra.
	///   * ROUPA -> rasgo. Pano nao fica roxo, ele abre -- e o buraco mostra a pele ferida que a
	///     camada de baixo ja pintou, que e o encaixe que faz o efeito valer a pena.
	///   * CABELO, OLHOS, RABO -> nada. Foi o que o dono pediu, e faz sentido: cabelo nao
	///     hematoma e olho nao rasga.
	/// ===========================================================================
	///
	/// So mexe nos uniformes quando a mascara MUDA. Ela vem do servidor a 5 Hz e so quando o
	/// corpo muda de cara -- reescrever dez parametros por quadro pra repetir o mesmo valor seria
	/// pagar o efeito inteiro em todo mundo da tela, o tempo todo.
	/// </summary>
	public void Ferir(Jandirus.Core.Combat.MascaraDeFeridas m, int semente)
	{
		if (_temFeridas && _feridas == m) return;
		_feridas = m;
		_temFeridas = true;
		_semente = (semente % 997) * 0.37f;

		var hema = new float[Jandirus.Core.Combat.MascaraDeFeridas.Zonas];
		var sang = new float[Jandirus.Core.Combat.MascaraDeFeridas.Zonas];
		for (int i = 0; i < hema.Length; i++)
		{
			var z = (Jandirus.Core.Combat.ZonaDeFerida)i;
			hema[i] = m.Hematoma(z);
			sang[i] = m.Sangue(z);
		}

		if (_corpo != null) AplicarFerida(_corpo, ModoPele, hema, sang);
		foreach (AnimatedSprite2D r in _roupa) AplicarFerida(r, ModoPano, hema, sang);
	}

	private void AplicarFerida(AnimatedSprite2D s, int modo, float[] hema, float[] sang)
	{
		if (!IsInstanceValid(s) || s.Material is not ShaderMaterial m) return;
		m.SetShaderParameter("ferida_modo", modo);
		m.SetShaderParameter("f_hema", hema);
		m.SetShaderParameter("f_sang", sang);
		m.SetShaderParameter("ferida_semente", _semente);
		AplicarAmputacao(s);
		AtualizarCaixa(s);
	}

	/// <summary>
	/// O LADO DO CORPO NAO E O LADO DA IMAGEM, e a diferenca inverte quando o boneco vira.
	///
	/// De frente (o sprite `south`), o braco ESQUERDO dele aparece a DIREITA de quem olha -- a
	/// mesma inversao de olhar alguem no espelho. De costas (`north`) os lados coincidem; de perfil
	/// so um braco aparece e tanto faz.
	///
	/// Sem esta traducao, arrancar o braco esquerdo apagaria o direito na tela metade das vezes, e
	/// o jogador veria o boneco discordando do paperdoll (que mostra o lado certo).
	/// </summary>
	private void AplicarAmputacao(AnimatedSprite2D s)
	{
		if (!IsInstanceValid(s) || s.Material is not ShaderMaterial m) return;

		bool espelha = _facing is Facing.South;
		Vector2 Lados(bool esq, bool dir) => espelha
			? new Vector2(dir ? 1 : 0, esq ? 1 : 0)
			: new Vector2(esq ? 1 : 0, dir ? 1 : 0);

		m.SetShaderParameter("amp_braco", Lados(
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.BracoEsq),
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.BracoDir)));
		m.SetShaderParameter("amp_perna", Lados(
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.PernaEsq),
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.PernaDir)));
	}

	/// <summary>
	/// A direcao mudou: os lados do corpo trocam de lado na imagem, entao a amputacao tem que ser
	/// reescrita. So faz trabalho se houver membro faltando.
	/// </summary>
	private void SeguirDirecaoNaAmputacao()
	{
		if (!_temFeridas || _feridas.Amputados == Jandirus.Core.Combat.MascaraDeFeridas.Membro.Nenhum) return;
		if (_corpo != null) AplicarAmputacao(_corpo);
		foreach (AnimatedSprite2D r in _roupa) AplicarAmputacao(r);
	}

	/// <summary>
	/// Reaplica a mascara depois de uma troca de roupa.
	///
	/// Camada de roupa NASCE limpa: `Vestir` cria um `AnimatedSprite2D` novo com material novo, e
	/// o material novo nao sabe de ferida nenhuma. Sem esta volta, trocar de camisa no meio de uma
	/// luta curava a roupa -- e trocar de roupa nao fecha ferimento.
	/// </summary>
	/// <summary>
	/// Em que modo cada familia de camada esta. SO PRA BANCADA (`--diagferida`).
	///
	/// Devolve (corpo, quantas roupas em modo pano, quantas OUTRAS camadas fora do modo 0) -- e a
	/// terceira que importa: ela e zero quando cabelo, olhos e rabo ficaram de fora, como o dono
	/// pediu. Sem isto, "so o corpo e a roupa recebem" seria afirmacao minha.
	/// </summary>
	public (int Corpo, int Roupas, int Outras) ModosDeTeste()
	{
		int Modo(AnimatedSprite2D? s) =>
			s != null && IsInstanceValid(s) && s.Material is ShaderMaterial m
				? (int)m.GetShaderParameter("ferida_modo") : -1;

		int roupas = _roupa.Count(r => Modo(r) == ModoPano);
		int outras = 0;
		foreach (AnimatedSprite2D? o in new[] { _cabelo, _olhos, _rabo })
			if (o != null && Modo(o) > 0) outras++;

		return (Modo(_corpo), roupas, outras);
	}

	/// <summary>O shader sabe que o desenho esta deitado? SO PRA BANCADA (`--diagferida`).</summary>
	public bool DeitadoDeTeste => _deitadoEnviado;

	/// <summary>A caixa do quadro que o corpo mandou pro shader. SO PRA BANCADA.</summary>
	public (float Min, float Max)? CaixaDeTeste()
	{
		if (_corpo is not { } s || !IsInstanceValid(s) || s.Material is not ShaderMaterial m) return null;
		Vector2 mn = m.GetShaderParameter("quadro_min").AsVector2();
		Vector2 mx = m.GetShaderParameter("quadro_max").AsVector2();
		return (mn.X + mn.Y, mx.X + mx.Y);
	}

	private void ReaplicarFeridas()
	{
		if (!_temFeridas) return;
		Jandirus.Core.Combat.MascaraDeFeridas m = _feridas;
		_temFeridas = false;   // forca o `Ferir` a passar pelo caminho inteiro
		Ferir(m, (int)(_semente / 0.37f));
	}

	/// <summary>Cor SOMADA a esta camada. Nulo = a cor natural do sprite.</summary>
	private static void Tingir(AnimatedSprite2D s, Rgb? cor)
	{
		if (s.Material is not ShaderMaterial m) return;
		Vector3 t = cor is { } c ? new Vector3(c.R / 255f, c.G / 255f, c.B / 255f) : Vector3.Zero;
		m.SetShaderParameter("tinta", t);
	}

	// =====================================================================
	// APARENCIA
	// =====================================================================
	/// <summary>
	/// Monta (ou remonta) as camadas a partir da ficha de aparencia. Pode ser chamado a cada
	/// mexida na tela de criacao -- e o que faz a previa ser AO VIVO.
	/// </summary>
	public void Vestir(VisualCatalog cat, Appearance ap, string raca, string genero)
	{
		Garantir();

		// --- corpo ---
		(Rgb? soma, float brilho) = cat.TintaDoCorpo(ap, raca);
		Trocar(_corpo!, cat.CorpoSprite(ap, raca, genero));
		_corpo!.SelfModulate = new Color(brilho, brilho, brilho);   // so o tom do Namekuseijin
		Tingir(_corpo, soma);

		// --- roupa: uma camada por peca, na ordem em que foi vestida ---
		while (_roupa.Count > ap.Roupa.Count)
		{
			AnimatedSprite2D velha = _roupa[^1];
			_roupa.RemoveAt(_roupa.Count - 1);
			Descartar(velha);
		}
		for (int i = 0; i < ap.Roupa.Count; i++)
		{
			if (i >= _roupa.Count) _roupa.Add(NovaCamada(2 + i));
			Trocar(_roupa[i], ap.Roupa[i]);
		}

		// --- cabelo: acima da roupa, como no jogo (plano 4 contra 3) ---
		string? cabelo = VisualCatalog.TemCabelo(raca) ? cat.SpriteDoCabelo(ap.Cabelo) : null;
		if (cabelo == null)
		{
			if (_cabelo != null) { Descartar(_cabelo); _cabelo = null; }
		}
		else
		{
			_cabelo ??= NovaCamada(10);
			Trocar(_cabelo, cabelo);
			Tingir(_cabelo, VisualCatalog.CabeloNatural(raca) ? null : ap.CorCabelo);
		}

		// --- olhos: um sprite so -- o jogo tem exatamente um arquivo ---
		if (cat.Olhos == null)
		{
			if (_olhos != null) { Descartar(_olhos); _olhos = null; }
		}
		else
		{
			_olhos ??= NovaCamada(11);
			Trocar(_olhos, cat.Olhos);
			Tingir(_olhos, ap.CorOlho);
		}

		MontarRabo();
		Reordenar();
		Aplicar(force: true);

		// A ROUPA NOVA NASCE LIMPA, e trocar de roupa nao fecha ferimento. Ver `ReaplicarFeridas`.
		ReaplicarFeridas();
	}

	/// <summary>
	/// O RABO aparece e some em jogo -- nao e escolha de criacao, e estado de corpo. Quem
	/// manda e o servidor (bit no snapshot), porque so ele sabe se o rabo ainda esta la.
	///
	/// Sem rabo o Saiyajin perde o Oozaru e treina 2,5x mais rapido (`tailgain`), entao isto
	/// nao e enfeite: e a leitura visual de uma mudanca grande de ficha.
	/// </summary>
	public void MostrarRabo(bool tem)
	{
		if (_temRabo == tem) return;
		_temRabo = tem;
		MontarRabo();
		Reordenar();
		if (_rabo != null) Aplicar(_rabo, _corpo?.Animation, force: true);
	}

	private void MontarRabo()
	{
		if (!_temRabo)
		{
			if (_rabo == null) return;
			Descartar(_rabo);
			_rabo = null;
			Reordenar();
			return;
		}

		// ENTRE O CORPO E A ROUPA. No original o rabo herda de CABELO no typepath
		// (`/obj/overlay/hairs/tails/saiyantail`) e engana quem le, mas o plano e o layer sao
		// recravados pra BODY_LAYER: ele desenha ACIMA do corpo e ABAIXO de tudo o mais.
		//
		// O numero e a posicao na PILHA (ver NovaCamada): 1 e logo acima do corpo, abaixo de
		// qualquer roupa. Quem materializa isso na arvore de nodes e Reordenar().
		_rabo ??= NovaCamada(1);
		Trocar(_rabo, SpriteDoRabo);
	}

	private static void Trocar(AnimatedSprite2D alvo, string caminho)
	{
		if (alvo.GetMeta("src", "").AsString() == caminho) return;   // ja e esse: nao reinicia a animacao
		var f = ResourceLoader.Load<SpriteFrames>(caminho);
		if (f == null) { GD.PushWarning($"[visual] sprite ausente: {caminho}"); return; }
		alvo.SpriteFrames = f;
		alvo.SetMeta("src", caminho);
		Ancorar(alvo, f);
	}

	/// <summary>O lado do tile. Toda folha de personagem normal e deste tamanho.</summary>
	private const int Celula = 32;

	/// <summary>
	/// ANCORA A CAMADA PELOS PES, e nao pelo centro.
	///
	/// Quase toda folha de personagem e 32x32 e o `Centered = true` acerta sozinho. Mas nao
	/// TODAS: o Big Broly e 32x64 e o Tyrone e 42x32. Centradas no mesmo ponto, uma folha de 64
	/// de altura desce 16 px em relacao as outras -- o cabelo e a roupa (32) ficam na cintura do
	/// corpo (64), e o personagem sai desmontado.
	///
	/// O BYOND ancora icone no canto INFERIOR ESQUERDO, e e por isso que la isso nunca
	/// aconteceu: la as folhas ja se encostam pelo chao e pela esquerda. Aqui a mesma regra
	/// precisa ser escrita -- o mesmo raciocinio do `texture_origin` do conversor de mapa, que
	/// existe pelo mesmo motivo nos tiles grandes.
	///
	/// Folha 32x32 da deslocamento zero, entao o caso comum nao paga nada.
	/// </summary>
	private static void Ancorar(AnimatedSprite2D alvo, SpriteFrames f)
	{
		foreach (string anim in f.GetAnimationNames())
		{
			if (f.GetFrameCount(anim) == 0) continue;
			if (f.GetFrameTexture(anim, 0) is not { } tex) continue;
			Vector2 t = tex.GetSize();
			if (t.X <= 0 || t.Y <= 0) return;
			// esquerda encostada na esquerda, base encostada na base
			alvo.Offset = new Vector2((Celula - t.X) * 0.5f, (Celula - t.Y) * 0.5f);
			return;
		}
	}

	// =====================================================================
	// ANIMACAO
	// =====================================================================
	/// <summary>Traduz a pose que veio do servidor no nome do estado de animacao.</summary>
	public void SetPose(Protocol.Pose pose) => SetState(pose switch
	{
		Protocol.Pose.Treinando => "train",
		Protocol.Pose.Meditando => "meditate",
		Protocol.Pose.Atacando => "attack",
		Protocol.Pose.Voando => "flight",
		Protocol.Pose.Nocauteado => "ko",
		_ => "default",
	});

	// =====================================================================
	// O CORPO DEITADO -- nocaute e arremesso
	// =====================================================================
	/// <summary>
	/// DEITA O SPRITE NA DIRECAO CERTA.
	///
	/// ============================ O PROBLEMA ============================
	/// O `.dmi` tem UM desenho de nocaute e ele cai sempre pro mesmo lado -- deitado pra direita.
	/// Isso vale quando o personagem estava olhando pra direita e fica errado nos outros tres
	/// casos: o dono viu o corpo cair pro mesmo lado independente de pra onde ele encarava.
	///
	/// Nao ha (e nao precisa haver) quatro desenhos de queda: o corpo deitado e simetrico o
	/// bastante pra que GIRAR resolva, e girar e o que o proprio BYOND fazia com `transform` no
	/// `Small_Impact`.
	/// ====================================================================
	///
	/// A CONTA E A QUE O DONO DITOU: olhando pra direita cai como hoje (0 graus); pra esquerda gira
	/// 180; pra cima gira 90 pra baixo; pra baixo gira 90 pra cima.
	/// </summary>
	public void DeitarPor(Facing olhando) => Girar(olhando switch
	{
		Facing.East => 0f,
		Facing.West => 180f,
		Facing.South => 90f,
		_ => -90f,
	});

	/// <summary>
	/// DEITA O CORPO **ACORDADO** na direcao do voo -- e o arremesso.
	///
	/// ============================ POR QUE E OUTRA TABELA ============================
	/// O <see cref="DeitarPor"/> serve ao sprite de NOCAUTE, que ja e um desenho DEITADO: a 0 grau
	/// a cabeca dele ja aponta pro LESTE, entao girar dali e so escolher pra que lado.
	///
	/// O arremesso usa o sprite ACORDADO (pedido do dono -- quem voa ainda esta consciente), e esse
	/// esta EM PE: a 0 grau a cabeca aponta pro NORTE. Aplicar a tabela do nocaute nele erra por 90
	/// graus, e o caso mais visivel e justamente o mais comum -- voando pra LESTE o angulo dava 0 e
	/// o corpo continuava DE PE. Foi o que o dono fotografou: "era pra girar o corpo e botar a
	/// cabeca nesse caso no lado direito enquanto ele voa".
	///
	/// A conta: rotacao positiva gira no sentido horario (o Y cresce pra baixo), e a cabeca em pe e
	/// o vetor (0,-1). Girar +90 leva (0,-1) pra (1,0) -- leste. Dai a tabela.
	/// ================================================================================
	/// </summary>
	public void VoarPara(Facing rumo) => Girar(rumo switch
	{
		Facing.North => 0f,
		Facing.East => 90f,
		Facing.South => 180f,
		_ => -90f,
	});

	/// <summary>
	/// GIRA O CORPO NA DIRECAO DO ARREMESSO -- o mesmo desenho deitado, apontado pra onde ele voa.
	///
	/// VETOR ZERO ENDIREITA. E o caminho de volta: fora do voo o sprite tem que estar no prumo, e
	/// deixar isso a cargo de quem chama seria uma chance a mais de o corpo ficar torto pra sempre.
	///
	/// E O SPRITE ACORDADO, nao o de nocaute -- pedido do dono, e faz sentido: quem esta voando
	/// ainda esta consciente, so nao esta no controle. Quem escolhe o estado e o chamador; aqui so
	/// se gira.
	/// </summary>
	public void GirarPara(Vec2 rumo)
	{
		if (rumo.LengthSquared < 1e-6f) { Girar(0f); return; }

		// QUATRO DIRECOES, NAO TRESENTAS E SESSENTA.
		//
		// A primeira versao usava o angulo cru do `atan2`, e o resultado foi o que o dono viu: "ao
		// levar knock back o personagem ta girando". Cada correcao do servidor mexia o rumo alguns
		// graus e o sprite acompanhava -- um corpo rodopiando no ar em vez de um corpo arremessado.
		//
		// O pedido e o mesmo do nocaute: "so virasse pro lado, dando uma rotacao no personagem
		// virando a cabeca dele pra direcao q ele ta voando". Quantizar no eixo dominante entrega
		// exatamente isso e ainda mata o tremor: um rumo que oscila 5 graus continua caindo na mesma
		// direcao, e o sprite fica parado.
		VoarPara(MoveRules.FacingFrom(rumo, Facing.East));
	}

	private void Girar(float graus)
	{
		if (Mathf.IsEqualApprox(RotationDegrees, graus)) return;
		RotationDegrees = graus;
	}

	public void SetState(string state)
	{
		if (_state == state) return;
		_state = state;
		_ritmo = 1;
		Aplicar(force: true);
	}

	/// <summary>
	/// Entra num estado REINICIANDO do primeiro quadro, mesmo se ja estivesse nele.
	///
	/// E o que da ritmo ao soco: sem isto, socar de novo enquanto a animacao roda nao faz
	/// nada visivel, e o ciclo que se repete sozinho parece um personagem TRAVADO socando
	/// pra sempre.
	/// </summary>
	public void RestartState(string state, double duracaoAlvo = 0)
	{
		_state = state;
		_ritmo = 1;
		Aplicar(force: true);   // ja zera o relogio: o golpe recomeca do primeiro quadro
		if (duracaoAlvo <= 0 || _corpo?.SpriteFrames is not { } f) return;

		// ENCAIXA A ANIMACAO NO TEMPO DO GOLPE. O .dmi traz o soco na cadencia do BYOND
		// (~0,67 s); com a cadencia nova de ~0,33 s a animacao nao terminaria antes do
		// proximo soco e o boneco pareceria empacado no meio do movimento. Esticar o relogio
		// conserta na raiz -- e como o mesmo relogio move TODAS as camadas, roupa e cabelo
		// aceleram junto sem sair de compasso.
		double ciclo = f.HasAnimation(_corpo.Animation) ? Ciclo(f, _corpo.Animation) : 0;
		if (ciclo > 0) _ritmo = Math.Clamp(ciclo / duracaoAlvo, 0.5, 6);
	}

	/// <summary>
	/// Escala do relogio da animacao. 1 = a duracao que veio do .dmi; 2 = o dobro da
	/// velocidade. Quem mexe nisto e o soco (ver <see cref="RestartState"/>).
	/// </summary>
	private double _ritmo = 1;

	public void SetMotion(Facing facing, bool moving)
	{
		if (_facing == facing && _moving == moving) return;
		bool virou = _facing != facing;
		_facing = facing;
		_moving = moving;
		Aplicar(force: false);
		// VIROU: o braco esquerdo dele mudou de lado na tela. Ver `AplicarAmputacao`.
		if (virou) SeguirDirecaoNaAmputacao();
	}

	// =====================================================================
	// A IMAGEM REMANESCENTE (Zanzoken)
	// =====================================================================
	/// <summary>
	/// UMA FOTOGRAFIA DESTE CORPO no instante atual -- o `image(icon=target, icon_state=..., dir=...)`
	/// que o original larga no chao (`Buff Effects.dm:41-46`).
	///
	/// COPIA A PILHA INTEIRA, e nao so o corpo: um fantasma sem roupa nem cabelo nao parece "voce
	/// que ficou pra tras", parece outra pessoa. A textura de cada camada e a MESMA (nao ha copia
	/// de pixels), entao um fantasma custa alguns Sprite2D e nada de memoria de imagem.
	///
	/// SAI SOLTO DA ARVORE deste no de proposito: o fantasma tem que ficar ONDE O CORPO ESTAVA
	/// enquanto o corpo continua andando. Filho, ele iria junto -- e "imagem remanescente" que
	/// acompanha o dono e so um borrao grudado.
	/// </summary>
	/// <param name="comTinta">
	/// Copiar tambem o material de tinta de cada camada. Quem vai TROCAR o material logo em
	/// seguida (o rastro de corrida, que aplica o borrao) pede `false` -- ver o comentario dentro.
	/// </param>
	public Node2D Fotografar(bool comTinta = true)
	{
		// A FOTO HERDA A ROTACAO. Sem isto, o vulto de quem esta voando (ou caido) nascia EM PE --
		// o `Girar` escreve no proprio `CharacterVisual`, que e o PAI das camadas, e a copia so
		// levava as camadas. Uma miragem em pe ao lado de um corpo deitado entrega o truque.
		var copia = new Node2D { Name = "Fantasma", Rotation = Rotation };
		foreach (AnimatedSprite2D s in _camadas)
		{
			if (!s.Visible || s.SpriteFrames is not { } f || !f.HasAnimation(s.Animation)) continue;
			Texture2D? tex = f.GetFrameTexture(s.Animation, s.Frame);
			if (tex == null) continue;

			var q = new Sprite2D
			{
				Texture = tex,
				Centered = s.Centered,
				Offset = s.Offset,
				Position = s.Position,
				FlipH = s.FlipH,
				TextureFilter = TextureFilterEnum.Nearest,
			};

			// A TINTA VAI JUNTO. Sem ela o cabelo do Super Saiyajin volta a preto no fantasma, e a
			// imagem remanescente de um SSJ sairia com o cabelo da forma base.
			//
			// ============================ SO SE ALGUEM FOR OLHAR ============================
			// O rastro de corrida TROCA este material por um de borrao no instante seguinte
			// (`BorraoDirecional.Aplicar`), entao pra ele a tinta e um `ShaderMaterial` criado e
			// jogado fora sem nunca ter desenhado nada. A 30 fotos por segundo e com quatro camadas
			// por foto, sao 120 materiais por segundo de puro desperdicio POR CORPO CORRENDO -- e o
			// suspeito numero um das travadinhas que o dono relatou, porque e o unico que roda o
			// tempo todo e justamente enquanto se anda.
			//
			// Quem vai borrar avisa (`comTinta: false`) e economiza a criacao inteira. Quem quer o
			// fantasma parado (a miragem do Zanzoken) continua pedindo a tinta.
			// ===============================================================================
			if (comTinta && s.Material is ShaderMaterial m)
			{
				var mat = new ShaderMaterial { Shader = ShaderTinta };
				mat.SetShaderParameter("tinta", m.GetShaderParameter("tinta"));
				q.Material = mat;
			}
			copia.AddChild(q);
		}
		return copia;
	}

	// =====================================================================
	// CORRIDA
	// =====================================================================
	/// <summary>Quanto o passo acelera a plena corrida. Casa com o `MultiplicadorCorrida` (2,2x).</summary>
	private const double RitmoDaCorrida = 2.2;

	private bool _correndo;

	/// <summary>
	/// ESTOU CORRENDO -- e com isso o passo acelera.
	///
	/// ============================ O BORRAO SAIU DAQUI ============================
	/// Havia um smear no shader: 4 amostras extras da PROPRIA textura ao longo do rumo. O dono
	/// reportou duas vezes que o personagem "pisca" correndo, e a segunda vez veio com o conserto
	/// junto: "n teria como o efeito ser um motion blur?".
	///
	/// Ele apontou a natureza do defeito. Aquilo NAO era motion blur -- era um borrao ESPACIAL
	/// calculado dentro do quadro atual da animacao. E como a animacao de corrida roda 2,2x mais
	/// rapido, cada troca de quadro trocava de uma vez todo o conteudo amostrado: o borrao dava um
	/// salto por quadro do .dmi, o que o olho le exatamente como piscada. Suavizar a direcao (o que
	/// tentei antes) nao alcanca isso, porque a descontinuidade nao esta na direcao -- esta na
	/// FONTE.
	///
	/// Motion blur de verdade e TEMPORAL: ele mostra onde o corpo ESTEVE. Quem faz isso agora e o
	/// <see cref="RastroDeCorrida"/>, que larga copias do corpo nas posicoes passadas. Copia velha
	/// guarda o quadro velho, entao trocar de quadro nao muda nada do que ja foi desenhado -- a
	/// piscada nao tem por onde nascer.
	/// ============================================================================
	/// </summary>
	public void Correr(bool correndo, Vector2 rumo) => _correndo = correndo;

	// O soco NAO volta ao normal por "animacao terminou": todo estado vindo do .dmi tem
	// loop=true (o BYOND repetia o ciclo eternamente), entao esse evento nunca dispararia.
	// Quem encerra a pose e o RELOGIO, dos dois lados.

	// =====================================================================
	// O ANIMADOR
	// =====================================================================
	// QUEM ANIMA E ESTA CLASSE, nao o AnimatedSprite2D. Nenhuma camada chama Play().
	//
	// A primeira tentativa foi "so o corpo toca e as outras copiam o quadro dele". Nao basta:
	// o avanco interno do sprite acontece num ponto do quadro e a copia noutro, entao as
	// camadas ficavam SEMPRE UM QUADRO ATRAS do corpo (medido: corpo no quadro 1 com todas as
	// camadas ainda no 0). Um relogio proprio elimina a corrida -- todas as camadas recebem o
	// quadro na MESMA passada, derivado do MESMO tempo.
	private double _relogio;

	/// <summary>Duracao total de uma animacao, em segundos.</summary>
	private static double Ciclo(SpriteFrames f, string anim)
	{
		if (!f.HasAnimation(anim)) return 0;
		double vel = Math.Max(f.GetAnimationSpeed(anim), 0.01);
		double total = 0;
		for (int i = 0; i < f.GetFrameCount(anim); i++) total += f.GetFrameDuration(anim, i) / vel;
		return total;
	}

	/// <summary>
	/// Em que quadro a animacao esta no instante <paramref name="t"/>, RESPEITANDO a duracao
	/// de cada quadro.
	///
	/// Nao da pra dividir o ciclo em partes iguais: o estado parado do corpo tem `delay =
	/// 1,1,1,30`, ou seja tres quadros de 0,1s (a piscada) e um segurando 3 segundos (de pe,
	/// olhos abertos). Distribuindo linearmente, cada quadro ganhava 0,82s e a piscada virava
	/// camera lenta -- foi o que o dono viu como "quase 5 segundos por piscada".
	/// </summary>
	private static int QuadroEm(SpriteFrames f, string anim, double t)
	{
		int n = f.GetFrameCount(anim);
		if (n <= 1) return 0;
		double vel = Math.Max(f.GetAnimationSpeed(anim), 0.01);

		double acc = 0;
		for (int i = 0; i < n; i++)
		{
			acc += f.GetFrameDuration(anim, i) / vel;
			if (t < acc) return i;
		}
		return n - 1;
	}

	// =====================================================================
	// IMPACTO
	// =====================================================================
	// O impacto e ANIMADO NO SHADER, em todas as camadas ao mesmo tempo -- corpo, roupa,
	// cabelo e rabo lavam juntos, que e o unico jeito de o personagem parecer UM objeto
	// levando um golpe em vez de uma pilha de sprites.
	//
	// A versao anterior usava o `Modulate` da raiz. Modulate MULTIPLICA: piscar de vermelho
	// ESCURECIA o boneco, ou seja levar um soco deixava o personagem mais apagado. Agora o
	// shader MISTURA em direcao a cor, o que clareia e preserva a silhueta.
	private double _flash, _flashTotal;
	private Vector2 _empurrao;

	/// <summary>
	/// Marca o impacto: lava a cor, acende o contorno, achata o corpo e empurra na direcao do
	/// golpe. <paramref name="direcao"/> vem normalizada (de quem bateu pra quem levou).
	/// </summary>
	public void Impacto(Color cor, Color contorno, Vector2 direcao, double segundos = 0.15)
	{
		_flash = _flashTotal = segundos;
		_empurrao = direcao * 3f;

		foreach (AnimatedSprite2D s in _camadas)
		{
			if (s.Material is not ShaderMaterial m) continue;
			m.SetShaderParameter("flash_cor", cor);
			m.SetShaderParameter("contorno_cor", contorno);
		}
		AplicarImpacto(1);
	}

	/// <summary>Escreve o estado do impacto em TODAS as camadas. 0 = corpo em repouso.</summary>
	private void AplicarImpacto(float f)
	{
		foreach (AnimatedSprite2D s in _camadas)
		{
			if (s.Material is not ShaderMaterial m) continue;
			m.SetShaderParameter("flash", f * 0.85f);
			m.SetShaderParameter("contorno", f);
			m.SetShaderParameter("achatar", f * 0.18f);
			m.SetShaderParameter("empurrao", _empurrao * f);
		}
	}

	public override void _Process(double delta)
	{
		if (_flash > 0)
		{
			_flash -= delta;
			AplicarImpacto(_flashTotal > 0 ? (float)Math.Max(_flash / _flashTotal, 0) : 0);
		}

		if (_corpo?.SpriteFrames == null) return;

		// TUDO anima, inclusive parado: a pose parada e um estado proprio com ciclo proprio
		// (a "respiracao"). Quem NAO anima e a camada que caiu numa pose emprestada.
		SpriteFrames? corpoF = _corpo.SpriteFrames;
		double ciclo = corpoF == null ? 0 : Ciclo(corpoF, _corpo.Animation);

		// O PASSO ACELERA NA CORRIDA. Sem isto o personagem desliza: as pernas andam na cadencia
		// de caminhada enquanto o corpo atravessa o dobro do chao, e o cerebro le como patinacao.
		// So vale enquanto ANDANDO -- correndo parado (empurrando parede) nao existe, e acelerar a
		// pose de respiracao daria um personagem ofegante de pe.
		double ritmo = _ritmo * (_correndo && _moving && _state == "default" ? RitmoDaCorrida : 1);
		_relogio = ciclo > 0 ? (_relogio + delta * ritmo) % ciclo : 0;

		double fase = ciclo > 0 ? _relogio / ciclo : 0;   // 0..1 dentro do ciclo do corpo

		foreach (AnimatedSprite2D s in _camadas)
		{
			SpriteFrames? f = s.SpriteFrames;
			if (f == null || !s.Visible || !f.HasAnimation(s.Animation)) continue;
			if (!s.GetMeta("sync", true).AsBool()) { if (s.Frame != 0) s.Frame = 0; continue; }

			// A MESMA FASE do corpo, lida no relogio DESTA camada: o `ClothesSaiyanSuit` tem
			// 8 quadros de caminhada onde o corpo tem 4, e cada folha pode ter duracoes
			// diferentes. Assim ninguem corre em velocidade errada nem sai de compasso.
			int alvo = QuadroEm(f, s.Animation, fase * Ciclo(f, s.Animation));
			if (s.Frame != alvo) s.Frame = alvo;
		}
	}

	private void Aplicar(bool force)
	{
		// O CORPO ESCOLHE A POSE PRIMEIRO e as camadas seguem o nome DELE quando podem.
		// Sem isso elas divergem por um detalhe dos .dmi: o corpo tem `train` (uma direcao
		// so) enquanto o cabelo e o `GokuDBSSuit` tem `train_east/north/south/west`. Cada um
		// escolhendo por conta propria acabava com o corpo em `train` e o cabelo em
		// `train_east` -- animacoes diferentes, contagens diferentes, tudo fora de compasso.
		string? doCorpo = _corpo == null ? null : Escolher(_corpo, null);
		foreach (AnimatedSprite2D s in _camadas) Aplicar(s, doCorpo, force);
		_relogio = 0;
		AvisarSeDeitado(doCorpo);
	}

	/// <summary>
	/// AS POSES EM QUE O CORPO ESTA DEITADO DENTRO DO QUADRO.
	///
	/// Medido: das 48 animacoes do corpo, so o `ko` e um desenho deitado (pes em x=0, cabeca em
	/// x=0.78). `flight`, `train` e `meditate` sao desenhos EM PE -- neles o node gira e o UV gira
	/// junto, e o shader nao precisa saber de nada.
	/// </summary>
	private static bool PoseDeitada(string? pose) =>
		pose != null && pose.StartsWith("ko", StringComparison.Ordinal);

	private bool _deitadoEnviado;

	/// <summary>
	/// Diz ao shader que o corpo virou de lado DENTRO do desenho.
	///
	/// Sem isto o dono via o defeito exato: "quando voce gira o personagem por knock back ou ko, o
	/// shader nao gira, com isso os ferimentos ficam no lugar errado (e a roupa tb fica errada)".
	/// Girar o NODE resolve a pose na tela e nao mexe no que esta desenhado no quadro -- e o quadro
	/// de nocaute ja vem deitado.
	/// </summary>
	private void AvisarSeDeitado(string? pose)
	{
		bool deitado = PoseDeitada(pose);
		if (deitado == _deitadoEnviado) return;
		_deitadoEnviado = deitado;

		foreach (AnimatedSprite2D s in _camadas)
			if (IsInstanceValid(s) && s.Material is ShaderMaterial m)
				m.SetShaderParameter("ferida_deitado", deitado ? 1f : 0f);
	}

	/// <summary>
	/// A pose desta camada. Ordem: o nome que o CORPO usou, depois a variante com direcao,
	/// depois a sem direcao, e so entao a substituta -- que tem que manter a DIRECAO.
	///
	/// A substituta importa porque 13 roupas nao tem pose de ataque e 68 nao tem a de treino.
	/// A versao antiga caia em `default_south` FIXO: a camisa aparecia de frente, em pose de
	/// caminhada, por cima de um corpo socando pro lado. Errar a direcao e muito mais visivel
	/// que emprestar a pose.
	/// </summary>
	/// <summary>O nome da animacao termina em `_north/_south/_east/_west`?</summary>
	private static bool TemSufixoDeDirecao(string nome) =>
		nome.EndsWith("_north", StringComparison.Ordinal) || nome.EndsWith("_south", StringComparison.Ordinal)
		|| nome.EndsWith("_east", StringComparison.Ordinal) || nome.EndsWith("_west", StringComparison.Ordinal);

	private string? Escolher(AnimatedSprite2D sprite, string? doCorpo)
	{
		SpriteFrames? f = sprite.SpriteFrames;
		if (f == null) return null;

		string fam = Familia();

		// ============================ A DIRECAO SAI DO CORPO, NAO DO FACING ============================
		// Quando a pose do CORPO nao tem sufixo de direcao -- e o caso do `train`, que no .dmi e uma
		// unica animacao virada pro SUL --, as camadas nao podem usar o facing do jogador: o corpo
		// vai estar de frente e o cabelo iria pro lado pra onde o personagem estava olhando quando
		// comecou a treinar.
		//
		// Foi o que o dono viu: "se eu treinar quando estava virado pra esquerda ou pra cima o
		// cabelo buga, ele n gira pro lado certo -- a animacao de train e sempre pra mesma direcao
		// entao n importa a posicao inicial do personagem".
		//
		// Regra: pose sem direcao no corpo => as camadas usam SUL, que e a direcao em que o BYOND
		// desenha um estado unico.
		bool corpoSemDirecao = doCorpo != null && !TemSufixoDeDirecao(doCorpo);
		string dir = corpoSemDirecao ? "south" : MoveRules.FacingSuffix(_facing);

		if (doCorpo != null && f.HasAnimation(doCorpo)) return doCorpo;
		if (f.HasAnimation($"{fam}_{dir}")) return $"{fam}_{dir}";
		if (f.HasAnimation(fam)) return fam;

		// APELIDO `_mov`. Estado com `movement = 1` no .dmi virou `<nome>_mov` na conversao, e
		// nem toda peca marcou o mesmo estado como de movimento -- o corpo tem
		// `flight_mov_south` e o rabo tambem, mas quem escrever "flight" nao acha nenhum dos
		// dois. Sem este apelido, voar deixaria o rabo em pose de caminhada.
		if (f.HasAnimation($"{fam}_mov_{dir}")) return $"{fam}_mov_{dir}";
		if (f.HasAnimation($"{fam}_mov")) return $"{fam}_mov";

		// peca sem POSE PARADA propria usa o primeiro quadro da caminhada, e vice-versa --
		// varias roupas so trazem uma das duas
		string outra = fam == "walk" ? "default" : fam == "default" ? "walk" : "";
		if (outra.Length > 0)
		{
			if (f.HasAnimation($"{outra}_{dir}")) return $"{outra}_{dir}";
			if (f.HasAnimation(outra)) return outra;
		}

		if (f.HasAnimation($"default_{dir}")) return $"default_{dir}";
		if (f.HasAnimation($"walk_{dir}")) return $"walk_{dir}";
		if (f.HasAnimation("default_south")) return "default_south";
		if (f.HasAnimation("walk_south")) return "walk_south";
		return null;
	}

	private void Aplicar(AnimatedSprite2D sprite, string? doCorpo, bool force)
	{
		if (sprite.SpriteFrames == null) return;

		string? nome = Escolher(sprite, doCorpo);
		if (nome == null)
		{
			// a peca nao tem nem uma pose parada nesta direcao: some, em vez de desenhar
			// qualquer coisa por cima do corpo
			sprite.Visible = false;
			return;
		}
		sprite.Visible = true;

		// "sincronizada" = esta na MESMA pose do corpo. Quem esta numa substituta fica
		// congelada no primeiro quadro em vez de acompanhar um ciclo que nao e o dela.
		bool naPose = nome.StartsWith(Familia(), StringComparison.Ordinal);
		sprite.SetMeta("sync", naPose);

		if (force || sprite.Animation != nome) sprite.Animation = nome;
		sprite.Stop();          // ninguem toca sozinho: o relogio desta classe manda em todos
		sprite.Frame = 0;
	}
}
